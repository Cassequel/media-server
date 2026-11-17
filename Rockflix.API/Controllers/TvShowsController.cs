using System.Security.Claims;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.DTOs.Media;
using Rockflix.API.Services;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TvShowsController(AppDbContext db, TmdbService tmdb, StreamCacheService cache) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var shows = await db.TvShows
            .Select(s => new TvShowDto
            {
                Id = s.Id,
                Title = s.Title,
                Year = s.Year,
                Description = s.Description,
                PosterPath = s.PosterPath,
                BackdropPath = s.BackdropPath,
                TmdbRating = s.TmdbRating,
                TmdbId = s.TmdbId,
                Genre = s.Genre,
                EpisodeCount = s.Episodes.Count,
                SeasonCount = s.Episodes.Select(e => e.SeasonNumber).Distinct().Count()
            })
            .ToListAsync();

        return Ok(shows);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var show = await db.TvShows
            .Where(s => s.Id == id)
            .Select(s => new TvShowDto
            {
                Id = s.Id,
                Title = s.Title,
                Year = s.Year,
                Description = s.Description,
                PosterPath = s.PosterPath,
                BackdropPath = s.BackdropPath,
                TmdbRating = s.TmdbRating,
                TmdbId = s.TmdbId,
                Genre = s.Genre,
                EpisodeCount = s.Episodes.Count,
                SeasonCount = s.Episodes.Select(e => e.SeasonNumber).Distinct().Count()
            })
            .FirstOrDefaultAsync();

        if (show == null) return NotFound();
        return Ok(show);
    }

    [HttpGet("{id}/credits")]
    public async Task<IActionResult> GetCredits(int id)
    {
        var show = await db.TvShows.FindAsync(id);
        if (show == null) return NotFound();
        if (show.TmdbId == null) return Ok(new TmdbCreditsResult());
        var credits = await tmdb.GetCreditsAsync(show.TmdbId.Value, isTv: true);
        return Ok(credits ?? new TmdbCreditsResult());
    }

    [HttpGet("{id}/episodes")]
    public async Task<IActionResult> GetEpisodes(int id)
    {
        var userId = UserId;
        var episodes = await db.Episodes
            .Where(e => e.TvShowId == id)
            .OrderBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .Select(e => new EpisodeDto
            {
                Id = e.Id,
                TvShowId = e.TvShowId,
                SeasonNumber = e.SeasonNumber,
                EpisodeNumber = e.EpisodeNumber,
                Title = e.Title,
                Description = e.Description,
                StillPath = e.StillPath,
                RuntimeMinutes = e.RuntimeMinutes,
                ProgressSeconds = db.WatchHistory
                    .Where(w => w.UserId == userId && w.EpisodeId == e.Id)
                    .Select(w => (long?)w.ProgressSeconds)
                    .FirstOrDefault(),
                Completed = db.WatchHistory
                    .Where(w => w.UserId == userId && w.EpisodeId == e.Id)
                    .Select(w => (bool?)w.Completed)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(episodes);
    }

    [HttpGet("episodes/{episodeId}")]
    public async Task<IActionResult> GetEpisode(int episodeId)
    {
        var userId = UserId;
        var episode = await db.Episodes.Where(e => e.Id == episodeId)
            .Select(e => new EpisodeDto
            {
                Id = e.Id,
                TvShowId = e.TvShowId,
                SeasonNumber = e.SeasonNumber,
                EpisodeNumber = e.EpisodeNumber,
                Title = e.Title,
                Description = e.Description,
                StillPath = e.StillPath,
                RuntimeMinutes = e.RuntimeMinutes,
                ProgressSeconds = db.WatchHistory
                    .Where(w => w.UserId == userId && w.EpisodeId == e.Id)
                    .Select(w => (long?)w.ProgressSeconds)
                    .FirstOrDefault(),
                Completed = db.WatchHistory
                    .Where(w => w.UserId == userId && w.EpisodeId == e.Id)
                    .Select(w => (bool?)w.Completed)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();
        return episode == null ? NotFound() : Ok(episode);
    }

    [HttpPost("{id}/prepare")]
    public async Task<IActionResult> PrepareShow(int id, [FromQuery] int? episodeId)
    {
        if (episodeId == null) return BadRequest("An explicit episodeId is required.");
        var file = await db.Episodes.Where(e => e.TvShowId == id && e.Id == episodeId)
            .Select(e => e.FilePath).FirstOrDefaultAsync();
        if (file == null || !System.IO.File.Exists(file)) return NotFound();
        await PrepareFileAsync(file);
        return Ok(new { prepared = episodeId.Value });
    }

    private async Task PrepareFileAsync(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return;
        var video = await ProbeCodecAsync(filePath, "v:0");
        var audio = await ProbeCodecAsync(filePath, "a:0");
        var videoCompatible = video == "h264";
        var audioCompatible = audio == "aac";
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (videoCompatible && audioCompatible && (ext == ".mp4" || ext == ".m4v")) return;
        await cache.GetOrCreateSeekableMp4Async(filePath, videoCompatible, audioCompatible);
    }

    private static async Task<string?> ProbeCodecAsync(string filePath, string streamSelector)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -select_streams {streamSelector} -show_entries stream=codec_name -of default=nw=1:nk=1 \"{filePath}\"",
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var codec = output.Trim().ToLowerInvariant();
        return codec.Length > 0 ? codec : null;
    }

    [HttpPost("episodes/{episodeId}/progress")]
    public async Task<IActionResult> UpdateProgress(int episodeId, [FromBody] ProgressRequest request)
    {
        var userId = UserId;
        var entry = await db.WatchHistory
            .FirstOrDefaultAsync(w => w.UserId == userId && w.EpisodeId == episodeId);

        if (entry == null)
        {
            db.WatchHistory.Add(new Models.WatchHistory
            {
                UserId = userId,
                EpisodeId = episodeId,
                ProgressSeconds = request.ProgressSeconds,
                Completed = request.Completed,
                LastWatched = DateTime.UtcNow
            });
        }
        else
        {
            entry.ProgressSeconds = request.ProgressSeconds;
            entry.Completed = request.Completed;
            entry.LastWatched = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return NoContent();
    }
}
