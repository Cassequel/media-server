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

    public record MediaRequestDto(string Text);
}
