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
    public async Task<IActionResult> GetUsers()
    {
        var users = await db.Users
            .Select(u => new { u.Id, u.Username, u.Email, u.IsAdmin, u.CreatedAt })
            .ToListAsync();
        return Ok(users);
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

    // --- Telegram User Management ---

    [HttpGet("telegram-users")]
    public async Task<IActionResult> GetTelegramUsers()
    {
        var users = await db.TelegramUsers
            .Include(u => u.Requests.OrderByDescending(r => r.RequestedAt).Take(20))
            .OrderByDescending(u => u.AuthorizedAt)
            .Select(u => new
            {
                u.ChatId,
                u.DisplayName,
                u.AuthorizedAt,
                u.IsActive,
                Requests = u.Requests
                    .OrderByDescending(r => r.RequestedAt)
                    .Take(20)
                    .Select(r => new
                    {
                        r.RequestText,
                        r.ResolvedTitle,
                        r.MediaType,
                        r.Success,
                        r.RequestedAt
                    })
            })
            .ToListAsync();

        return Ok(users);
    }

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
}
