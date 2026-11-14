using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Services.Filtering;

namespace Rockflix.API.Controllers;

[ApiController, Route("api/movie-filtering"), Authorize]
public class MovieFilteringController(AppDbContext db, MovieTimestampService provider, FilterRequestService requests) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new { enabled = requests.Enabled, primaryAudioLanguage = "English", maxAssessmentSeconds = 120 });

    [HttpGet("{tmdbId:int}/versions")]
    public async Task<IActionResult> Versions(int tmdbId) => Ok(await db.MovieVersions.Where(v => v.Movie.TmdbId == tmdbId)
        .Select(v => new { v.Id, v.MovieId, v.RemoveSex, v.MuteLanguage, v.AudioLanguage, v.Notice, v.DurationSeconds }).ToListAsync());

    [HttpGet("{tmdbId:int}/assessment")]
    public async Task<IActionResult> Assessment(int tmdbId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == UserId && u.IsActive, ct)) return Forbid();
        if (!requests.Enabled) return BadRequest(new { message = "Movie filtering is not enabled yet." });
        if (tmdbId <= 0) return BadRequest();
        try
        {
            var result = await provider.AssessAsync(tmdbId, ct);
            return Ok(new { result.TmdbId, result.Sex, result.Language, result.SourceUrl, result.FetchedAt });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return StatusCode(504, new { message = "Timestamp lookup timed out. Retry or choose original." }); }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or System.Text.Json.JsonException)
        { return StatusCode(503, new { message = "Timestamp source unavailable. Retry or choose original." }); }
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs() => Ok(await db.MediaRequests.Where(r => r.UserId == UserId && r.FilterJobId != null)
        .OrderByDescending(r => r.RequestedAt).Take(50).Select(r => new { id = r.FilterJobId, r.ResolvedTitle,
            r.FilterJob!.Status, r.FilterJob.Message, r.FilterJob.RemoveSex, r.FilterJob.MuteLanguage, r.FilterJob.Notice,
            r.FilterJob.MovieVersionId, movieId = r.FilterJob.MovieVersion == null ? (int?)null : r.FilterJob.MovieVersion.MovieId,
            r.FilterJob.SourceCleaned }).ToListAsync());

    [HttpPost("jobs/{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id)
    {
        if (!await db.Users.AnyAsync(u => u.Id == UserId && u.IsActive)) return Forbid();
        if (!requests.Enabled) return BadRequest(new { message = "Movie processing is disabled." });
        var request = await db.MediaRequests.Include(r => r.FilterJob).FirstOrDefaultAsync(r => r.UserId == UserId && r.FilterJobId == id);
        if (request?.FilterJob == null) return NotFound();
        var job = request.FilterJob;
        if (job.Status != "needs_decision") return Conflict(new { message = "This job is not waiting for a decision." });
        // A failed external search has an uncertain side effect; do not issue another automatically.
        if (string.IsNullOrEmpty(job.SourcePath)) return Conflict(new { message = "Acquisition needs administrator attention in Radarr before it can be retried." });
        job.Status = "checking"; job.Message = "Retrying media checks"; job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = job.Message });
    }

    [HttpPost("jobs/{id:guid}/original")]
    public async Task<IActionResult> ChooseOriginal(Guid id, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == UserId && u.IsActive, ct)) return Forbid();
        if (!requests.Enabled) return BadRequest(new { message = "Movie processing is disabled." });
        var request = await db.MediaRequests.Include(r => r.FilterJob).FirstOrDefaultAsync(r => r.UserId == UserId && r.FilterJobId == id, ct);
        if (request?.FilterJob == null) return NotFound();
        if (request.FilterJob.Status != "needs_decision") return Conflict(new { message = "This job is not waiting for a decision." });
        if (string.IsNullOrEmpty(request.FilterJob.SourcePath)) return Conflict(new { message = "The acquisition status is uncertain. An administrator must reconcile it in Radarr before another version can be requested." });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({request.FilterJob.TmdbId})", ct);
        var remaining = await db.MediaRequests.Include(r => r.User).Where(r => r.FilterJobId == id && r.Id != request.Id && r.Status == "pending").OrderBy(r => r.Id).ToListAsync(ct);
        // Before publication there is still only one requested output. An explicit switch to original can use this untouched source.
        if (remaining.Count == 0 && !await db.MovieVersions.AnyAsync(v => v.Movie.TmdbId == request.FilterJob.TmdbId && !v.RemoveSex && !v.MuteLanguage, ct)
            && !await db.MovieFilterJobs.AnyAsync(j => j.Id != id && j.TmdbId == request.FilterJob.TmdbId && !j.RemoveSex && !j.MuteLanguage && j.Status != "ready" && j.Status != "cancelled" && j.Status != "failed", ct))
        {
            request.FilterJob.RemoveSex = false; request.FilterJob.MuteLanguage = false;
            request.FilterJob.PlanJson = "[]"; request.FilterJob.Notice = ""; request.FilterJob.AcceptUnverified = false;
            request.FilterJob.Status = "checking"; request.FilterJob.Message = "Original selected; validating the existing staged source.";
            request.FilterJob.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Ok(new FilterRequestResult(request.FilterJob.Message, JobId: id));
        }
        if (request.ReservedBytes > 0 && remaining.Count > 0)
        {
            var next = remaining[0];
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({-(long)next.UserId})", ct);
            long used = await db.MediaRequests.Where(r => r.UserId == next.UserId).SumAsync(r => (r.FileSizeBytes ?? 0) + r.ReservedBytes, ct);
            if (!next.User.IsAdmin && used + request.ReservedBytes > 20L * 1024 * 1024 * 1024)
                return Conflict(new { message = "This is a shared request and its storage reservation cannot be transferred. You can request an original separately through the movie request form." });
            next.ReservedBytes = request.ReservedBytes;
        }
        // Withdraw only this viewer, transferring the shared quota reservation when necessary.
        request.Status = "cancelled";
        request.ReservedBytes = 0;
        if (remaining.Count == 0)
        {
            request.FilterJob.Status = "cancelled";
            request.FilterJob.UpdatedAt = DateTime.UtcNow;
            request.FilterJob.Message = "Cancelled; staged processing files expire after 24 hours. Torrent sources follow existing seeding rules.";
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        try { return Ok(await requests.ConfirmAsync(UserId, request.FilterJob.TmdbId, request.FilterJob.Title, new(), ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
