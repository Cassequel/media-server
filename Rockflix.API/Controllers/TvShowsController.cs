using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.DTOs.Media;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TvShowsController(AppDbContext db) : ControllerBase
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
                Genre = s.Genre,
                EpisodeCount = s.Episodes.Count,
                SeasonCount = s.Episodes.Select(e => e.SeasonNumber).Distinct().Count()
            })
            .FirstOrDefaultAsync();

        if (show == null) return NotFound();
        return Ok(show);
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
