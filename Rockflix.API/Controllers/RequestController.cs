using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;
using Rockflix.API.Services;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/request")]
[Authorize]
public class RequestController(MediaRequestService mediaRequestService, AppDbContext db) : ControllerBase
{
    private const long LimitBytes = 20L * 1024 * 1024 * 1024;

    [HttpPost("parse")]
    public async Task<IActionResult> ParseRequest([FromBody] MediaRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Text))
            return BadRequest(new { message = "Text is required." });

        var parsed = await mediaRequestService.ParseRequestAsync(dto.Text);
        if (parsed is null)
            return Ok(new { found = false });

        return Ok(new { found = true, title = parsed.Title, mediaType = parsed.MediaType, season = parsed.Season });
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmRequest([FromBody] ConfirmRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.MediaType))
            return BadRequest(new { message = "Title and media type are required." });

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.FindAsync(userId);
        if (user is null) return Unauthorized();
        if (!user.IsActive) return Forbid();

        if (!user.IsAdmin)
        {
            var used = await db.MediaRequests
                .Where(r => r.UserId == userId)
                .SumAsync(r => (long?)(r.FileSizeBytes ?? 0)) ?? 0;
            if (used >= LimitBytes)
                return BadRequest(new { message = "You've reached your 20 GB download limit." });
        }

        var result = await mediaRequestService.ProcessParsedAsync(dto.Title, dto.MediaType, dto.Season);

        db.MediaRequests.Add(new MediaRequest
        {
            UserId = userId,
            RequestText = dto.Title,
            MediaType = result.MediaType,
            ResolvedTitle = result.ResolvedTitle,
            ExternalId = result.ExternalId,
            Status = result.ExternalId.HasValue ? "pending" : "failed"
        });
        await db.SaveChangesAsync();

        return Ok(new { message = result.Message });
    }

    [HttpPost]
    public async Task<IActionResult> RequestMedia([FromBody] MediaRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Text))
            return BadRequest(new { message = "Text is required." });

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.FindAsync(userId);

        if (user is null) return Unauthorized();

        if (!user.IsActive)
            return Forbid();

        if (!user.IsAdmin)
        {
            var used = await db.MediaRequests
                .Where(r => r.UserId == userId)
                .SumAsync(r => (long?)(r.FileSizeBytes ?? 0)) ?? 0;

            if (used >= LimitBytes)
                return BadRequest(new { message = "You've reached your 20 GB download limit." });
        }

        var result = await mediaRequestService.ProcessRequestAsync(dto.Text);

        db.MediaRequests.Add(new MediaRequest
        {
            UserId = userId,
            RequestText = dto.Text,
            MediaType = result.MediaType,
            ResolvedTitle = result.ResolvedTitle,
            ExternalId = result.ExternalId,
            Status = result.ExternalId.HasValue ? "pending" : "failed"
        });
        await db.SaveChangesAsync();

        return Ok(new { message = result.Message });
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var usedBytes = await db.MediaRequests
            .Where(r => r.UserId == userId)
            .SumAsync(r => (long?)(r.FileSizeBytes ?? 0)) ?? 0;

        const long limitBytes = 20L * 1024 * 1024 * 1024;
        return Ok(new { usedBytes, limitBytes });
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var pending = await db.MediaRequests
            .Where(r => r.UserId == userId && r.Status == "pending" && r.ResolvedTitle != null)
            .OrderByDescending(r => r.RequestedAt)
            .Select(r => new { r.Id, r.ResolvedTitle, r.MediaType, r.RequestedAt })
            .ToListAsync();

        // Deduplicate by title+type in memory
        pending = pending
            .GroupBy(r => new { r.ResolvedTitle, r.MediaType })
            .Select(g => g.First())
            .ToList();
        return Ok(pending);
    }

    public record MediaRequestDto(string Text);
    public record ConfirmRequestDto(string Title, string MediaType, int? Season);
}
