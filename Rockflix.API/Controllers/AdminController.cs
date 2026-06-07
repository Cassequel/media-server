using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;
using Rockflix.API.Services;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController(AppDbContext db, MediaScannerService scanner) : ControllerBase
{
    [HttpPost("scan")]
    public async Task<IActionResult> ScanMedia()
    {
        await scanner.ScanAsync();
        return Ok(new { message = "Media scan complete." });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers()
    {
        const long limitBytes = 20L * 1024 * 1024 * 1024;

        var webUsers = await db.Users
            .Include(u => u.Requests)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        var linkedChatIds = webUsers
            .Where(u => u.TelegramChatId.HasValue)
            .Select(u => u.TelegramChatId!.Value)
            .ToHashSet();

        var telegramOnly = await db.TelegramUsers
            .Include(u => u.Requests)
            .Where(u => !linkedChatIds.Contains(u.ChatId))
            .OrderByDescending(u => u.AuthorizedAt)
            .ToListAsync();

        var result = webUsers.Select(u => new UserDto(
            Id: u.Id,
            Source: "web",
            Username: u.Username,
            Email: u.Email,
            IsAdmin: u.IsAdmin,
            IsActive: u.IsActive,
            CreatedAt: u.CreatedAt,
            TelegramChatId: u.TelegramChatId,
            IsTelegram: u.TelegramChatId.HasValue,
            RequestCount: u.Requests.Count,
            TotalSizeBytes: u.Requests.Sum(r => r.FileSizeBytes ?? 0),
            LimitBytes: u.IsAdmin ? (long?)null : limitBytes
        )).Concat(telegramOnly.Select(t => new UserDto(
            Id: null,
            Source: "telegram",
            Username: t.DisplayName ?? $"Telegram {t.ChatId}",
            Email: null,
            IsAdmin: false,
            IsActive: t.IsActive,
            CreatedAt: t.AuthorizedAt,
            TelegramChatId: t.ChatId,
            IsTelegram: true,
            RequestCount: t.Requests.Count,
            TotalSizeBytes: 0,
            LimitBytes: limitBytes
        ))).ToList();

        return Ok(result);
    }

    [HttpPatch("users/{id}/revoke")]
    public async Task<IActionResult> RevokeUser(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();
        user.IsActive = false;
        await db.SaveChangesAsync();
        return Ok(new { user.Id, user.IsActive });
    }

    [HttpPatch("users/{id}/restore")]
    public async Task<IActionResult> RestoreUser(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();
        user.IsActive = true;
        await db.SaveChangesAsync();
        return Ok(new { user.Id, user.IsActive });
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (user.IsAdmin) return BadRequest(new { message = "Cannot delete an admin user." });
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("users/{id}/admin")]
    public async Task<IActionResult> ToggleAdmin(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();
        user.IsAdmin = !user.IsAdmin;
        await db.SaveChangesAsync();
        return Ok(new { user.Id, user.Username, user.IsAdmin });
    }

    // Keep telegram-only revoke/restore for users not linked to a web account
    [HttpPatch("telegram-users/{chatId}/revoke")]
    public async Task<IActionResult> RevokeTelegramUser(long chatId)
    {
        var user = await db.TelegramUsers.FirstOrDefaultAsync(u => u.ChatId == chatId);
        if (user == null) return NotFound();
        user.IsActive = false;
        await db.SaveChangesAsync();
        return Ok(new { user.ChatId, user.IsActive });
    }

    [HttpPatch("telegram-users/{chatId}/restore")]
    public async Task<IActionResult> RestoreTelegramUser(long chatId)
    {
        var user = await db.TelegramUsers.FirstOrDefaultAsync(u => u.ChatId == chatId);
        if (user == null) return NotFound();
        user.IsActive = true;
        await db.SaveChangesAsync();
        return Ok(new { user.ChatId, user.IsActive });
    }

    private record UserDto(
        int? Id,
        string Source,
        string Username,
        string? Email,
        bool IsAdmin,
        bool IsActive,
        DateTime CreatedAt,
        long? TelegramChatId,
        bool IsTelegram,
        int RequestCount,
        long TotalSizeBytes,
        long? LimitBytes);
}
