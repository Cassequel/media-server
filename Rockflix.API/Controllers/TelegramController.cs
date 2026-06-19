using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;
using Rockflix.API.Services;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/telegram")]
public class TelegramController : ControllerBase
{
    // Keyed by chat ID; holds the parsed lookup waiting for user confirmation
    private static readonly ConcurrentDictionary<long, MediaRequestService.ParsedLookup> _pendingConfirmations = new();

    private readonly MediaRequestService _mediaRequestService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly ILogger<TelegramController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public TelegramController(
        MediaRequestService mediaRequestService,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        AppDbContext db,
        ILogger<TelegramController> logger,
        IServiceScopeFactory scopeFactory)
    {
        _mediaRequestService = mediaRequestService;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _db = db;
        _logger = logger;
        _scopeFactory = scopeFactory;
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

        // Respond to Telegram immediately — if we take > 60s, Telegram retries and the user
        // gets duplicate responses hours later. Process everything in the background instead.
        _ = Task.Run(() => ProcessAndReplyAsync(chatId.Value, text));
        return Ok();
    }

    private async Task ProcessAndReplyAsync(long chatId, string text)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Check 20GB limit for linked web users (admins are exempt)
            var linkedUser = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId);
            if (linkedUser is not null && !linkedUser.IsAdmin)
            {
                const long limitBytes = 20L * 1024 * 1024 * 1024;
                var used = await db.MediaRequests
                    .Where(r => r.UserId == linkedUser.Id)
                    .SumAsync(r => (long?)(r.FileSizeBytes ?? 0)) ?? 0;
                if (used >= limitBytes)
                {
                    await SendMessageAsync(chatId, "You've reached your 20 GB download limit.");
                    return;
                }
            }

            var lowerText = text.ToLowerInvariant();

            // Handle confirmation responses
            if (lowerText is "yes" or "y" or "confirm" or "yep" or "yeah")
            {
                if (!_pendingConfirmations.TryRemove(chatId, out var pending))
                {
                    await SendMessageAsync(chatId, "No pending request to confirm. Just tell me what you want to watch!");
                    return;
                }

                var result = await _mediaRequestService.ProcessConfirmedAsync(
                    pending.Title, pending.MediaType, pending.Season, pending.TmdbId, pending.TvdbId);

                db.TelegramRequests.Add(new TelegramRequest
                {
                    ChatId = chatId,
                    RequestText = pending.Title,
                    ResolvedTitle = result.ResolvedTitle,
                    MediaType = string.IsNullOrEmpty(result.MediaType) ? null : result.MediaType,
                    Success = result.ExternalId.HasValue,
                    RequestedAt = DateTime.UtcNow
                });

                if (linkedUser is not null && result.ExternalId.HasValue)
                {
                    db.MediaRequests.Add(new MediaRequest
                    {
                        UserId = linkedUser.Id,
                        RequestText = pending.Title,
                        MediaType = result.MediaType,
                        ResolvedTitle = result.ResolvedTitle,
                        ExternalId = result.ExternalId,
                        Status = "pending"
                    });
                }

                await db.SaveChangesAsync();
                await SendMessageAsync(chatId, result.Message);
                return;
            }

            // Handle cancellation responses
            if (lowerText is "no" or "n" or "cancel" or "nope")
            {
                _pendingConfirmations.TryRemove(chatId, out _);
                await SendMessageAsync(chatId, "Cancelled. What else would you like to watch?");
                return;
            }

            // New request — parse and look up, then ask for confirmation
            var lookup = await _mediaRequestService.ParseAndLookupAsync(text);

            if (lookup is null)
            {
                await SendMessageAsync(chatId, "Sorry, I couldn't find that. Try something like: 'Dune Part Two' or 'season 2 of Severance'.");
                return;
            }

            if (lookup.Blocked)
            {
                await SendMessageAsync(chatId, "Adult content, pornographic material, and NC-17 rated titles cannot be requested on this server.");
                return;
            }

            _pendingConfirmations[chatId] = lookup;
            var seasonMsg = lookup.Season.HasValue ? $" Season {lookup.Season}" : "";
            await SendMessageAsync(chatId, $"I found: {lookup.Title}{seasonMsg}. Reply 'yes' to confirm or 'no' to cancel.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed processing request from {ChatId}", chatId);
            await SendMessageAsync(chatId, "Something went wrong processing your request. Try again.");
        }
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
