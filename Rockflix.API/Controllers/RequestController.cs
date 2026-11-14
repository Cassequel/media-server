using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;
using Rockflix.API.Services;
using Rockflix.API.Services.Filtering;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/request")]
[Authorize]
public class RequestController(MediaRequestService mediaRequestService, AppDbContext db, FilterRequestService filtering) : ControllerBase
{
    private const long LimitBytes = 20L * 1024 * 1024 * 1024;

    [HttpPost("parse")]
    public async Task<IActionResult> ParseRequest([FromBody] MediaRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Text))
            return BadRequest(new { message = "Text is required." });

        var lookup = await mediaRequestService.ParseAndLookupAsync(dto.Text);
        if (lookup is null)
            return Ok(new { found = false, blocked = false });
        if (lookup.Blocked)
            return Ok(new { found = false, blocked = true });

        return Ok(new { found = true, blocked = false, title = lookup.Title, mediaType = lookup.MediaType, season = lookup.Season, tmdbId = lookup.TmdbId, tvdbId = lookup.TvdbId });
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

        if (dto.MediaType == "movie" && filtering.Enabled)
        {
            try { return Ok(await filtering.ConfirmAsync(userId, dto.TmdbId ?? 0, dto.Title,
                new(dto.RemoveSex, dto.MuteLanguage, dto.AcceptUnverified), HttpContext.RequestAborted)); }
            catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
            { return StatusCode(504, new { message = "Timestamp lookup timed out. Retry or choose original." }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }
        if (dto.RemoveSex || dto.MuteLanguage) return BadRequest(new { message = "Filtering is not enabled for this request." });

        if (!user.IsAdmin)
        {
            var used = await db.MediaRequests
                .Where(r => r.UserId == userId)
                .SumAsync(r => (long?)((r.FileSizeBytes ?? 0) + r.ReservedBytes)) ?? 0;
            if (used >= LimitBytes)
                return BadRequest(new { message = "You've reached your 20 GB download limit." });
        }

        var result = await mediaRequestService.ProcessConfirmedAsync(dto.Title, dto.MediaType, dto.Season, dto.TmdbId, dto.TvdbId);

        if (result.Existing) return Ok(new { message = result.Message });

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

        if (filtering.Enabled)
        {
            var lookup = await mediaRequestService.ParseAndLookupAsync(dto.Text);
            if (lookup?.MediaType == "movie" && lookup.TmdbId.HasValue && !lookup.Blocked)
            {
                try { return Ok(await filtering.ConfirmAsync(userId, lookup.TmdbId.Value, lookup.Title, new(), HttpContext.RequestAborted)); }
                catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            }
        }

        if (!user.IsAdmin)
        {
            var used = await db.MediaRequests
                .Where(r => r.UserId == userId)
                .SumAsync(r => (long?)((r.FileSizeBytes ?? 0) + r.ReservedBytes)) ?? 0;

            if (used >= LimitBytes)
                return BadRequest(new { message = "You've reached your 20 GB download limit." });
        }

        var result = await mediaRequestService.ProcessRequestAsync(dto.Text);

        if (result.Existing) return Ok(new { message = result.Message });

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
            .SumAsync(r => (long?)((r.FileSizeBytes ?? 0) + r.ReservedBytes)) ?? 0;

        const long limitBytes = 20L * 1024 * 1024 * 1024;
        return Ok(new { usedBytes, limitBytes });
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var pending = await db.MediaRequests
            .Where(r => r.UserId == userId && r.Status == "pending" && r.ResolvedTitle != null && r.FilterJobId == null)
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

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemovePending(int id)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var request = await db.MediaRequests
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId && r.Status == "pending");

        if (request is null)
            return NotFound();

        db.MediaRequests.Remove(request);
        await db.SaveChangesAsync();
        return NoContent();
    }

    public record MediaRequestDto(string Text);
    public record ConfirmRequestDto(string Title, string MediaType, int? Season, int? TmdbId, int? TvdbId,
        bool RemoveSex = false, bool MuteLanguage = false, bool AcceptUnverified = false);
}
