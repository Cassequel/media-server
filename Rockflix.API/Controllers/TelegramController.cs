using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;
using Rockflix.API.Services;
using System.Text.Json.Serialization;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/telegram")]
public class TelegramController : ControllerBase
{
    private readonly MediaRequestService _mediaRequestService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly ILogger<TelegramController> _logger;

    public TelegramController(
        MediaRequestService mediaRequestService,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        AppDbContext db,
        ILogger<TelegramController> logger)
    {
        _mediaRequestService = mediaRequestService;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _db = db;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Webhook([FromBody] TelegramUpdate update)
    {
        var text = update.Message?.Text?.Trim();
        var chatId = update.Message?.Chat?.Id;
        var firstName = update.Message?.From?.FirstName;

        if (string.IsNullOrWhiteSpace(text) || chatId is null)
            return Ok();

        _logger.LogInformation("Telegram message from chat {ChatId}: {Text}", chatId, text);

        // Handle /start <invite_code>
        if (text.StartsWith("/start"))
        {
            var parts = text.Split(' ', 2);
            var code = parts.Length > 1 ? parts[1].Trim() : null;
            var validCode = _config["Telegram:InviteCode"];

            if (code == validCode)
            {
                var existing = await _db.TelegramUsers.FirstOrDefaultAsync(u => u.ChatId == chatId.Value);
                if (existing is null)
                {
                    _db.TelegramUsers.Add(new TelegramUser
                    {
                        ChatId = chatId.Value,
                        DisplayName = firstName,
                        AuthorizedAt = DateTime.UtcNow,
                        IsActive = true
                    });
                    await _db.SaveChangesAsync();
                    await SendMessageAsync(chatId.Value, $"Welcome, {firstName}! You're authorized. Just text me what you want to watch and I'll add it.");
                }
                else if (!existing.IsActive)
                {
                    await SendMessageAsync(chatId.Value, "Your access has been revoked. Contact the admin.");
                }
                else
                {
                    await SendMessageAsync(chatId.Value, "You're already authorized! Just tell me what you want to watch.");
                }
            }
            else
            {
                _logger.LogWarning("Unauthorized /start attempt from chat {ChatId}", chatId);
            }

            return Ok();
        }

        // Check authorization and active status
        var telegramUser = await _db.TelegramUsers.FirstOrDefaultAsync(u => u.ChatId == chatId.Value);
        if (telegramUser is null || !telegramUser.IsActive)
        {
            _logger.LogWarning("Blocked message from chat {ChatId} (unauthorized or revoked)", chatId);
            return Ok();
        }

        // Process the request
        string result;
        bool success = true;
        string? resolvedTitle = null;
        string? mediaType = null;

        try
        {
            result = await _mediaRequestService.ProcessRequestAsync(text);
            // Try to extract resolved title from the response for logging
            if (result.Contains("Added '"))
            {
                resolvedTitle = result.Split('\'').ElementAtOrDefault(1);
                mediaType = result.Contains("Season") ? "tv" : "movie";
            }
            else if (result.Contains("already in your library"))
            {
                resolvedTitle = result.Split('\'').ElementAtOrDefault(1);
                mediaType = result.Contains("Season") ? "tv" : "movie";
            }
            else
            {
                success = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed processing request from {ChatId}", chatId);
            result = "Something went wrong processing your request. Try again.";
            success = false;
        }

        // Log the request
        _db.TelegramRequests.Add(new TelegramRequest
        {
            ChatId = chatId.Value,
            RequestText = text,
            ResolvedTitle = resolvedTitle,
            MediaType = mediaType,
            Success = success,
            RequestedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await SendMessageAsync(chatId.Value, result);
        return Ok();
    }

    private async Task SendMessageAsync(long chatId, string text)
    {
        var http = _httpClientFactory.CreateClient("telegram");
        var token = _config["Telegram:BotToken"]!;

        var payload = new { chat_id = chatId, text };
        var response = await http.PostAsJsonAsync(
            $"https://api.telegram.org/bot{token}/sendMessage", payload);

        if (!response.IsSuccessStatusCode)
            _logger.LogError("Telegram sendMessage failed: {Status}", response.StatusCode);
    }

    public record TelegramUpdate(
        [property: JsonPropertyName("update_id")] long UpdateId,
        [property: JsonPropertyName("message")] TelegramMessage? Message);

    public record TelegramMessage(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("chat")] TelegramChat? Chat,
        [property: JsonPropertyName("from")] TelegramFrom? From);

    public record TelegramChat(
        [property: JsonPropertyName("id")] long Id);

    public record TelegramFrom(
        [property: JsonPropertyName("first_name")] string? FirstName);
}
