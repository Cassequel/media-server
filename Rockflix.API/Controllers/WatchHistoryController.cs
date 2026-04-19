using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WatchHistoryController(AppDbContext db) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Returns the user's in-progress items for "Continue Watching".</summary>
    [HttpGet("continue-watching")]
    public async Task<IActionResult> GetContinueWatching()
    {
        var userId = UserId;

        var movies = await db.WatchHistory
            .Where(w => w.UserId == userId && w.MovieId != null && !w.Completed && w.ProgressSeconds > 0)
            .OrderByDescending(w => w.LastWatched)
            .Take(20)
            .Select(w => new
            {
                Type = "movie",
                w.MovieId,
                Title = w.Movie!.Title,
                PosterPath = w.Movie.PosterPath,
                BackdropPath = w.Movie.BackdropPath,
                w.ProgressSeconds,
                RuntimeSeconds = (long?)(w.Movie.RuntimeMinutes * 60),
                w.LastWatched
            })
            .ToListAsync();

        var episodes = await db.WatchHistory
            .Where(w => w.UserId == userId && w.EpisodeId != null && !w.Completed && w.ProgressSeconds > 0)
            .OrderByDescending(w => w.LastWatched)
            .Take(20)
            .Select(w => new
            {
                Type = "episode",
                w.EpisodeId,
                TvShowId = w.Episode!.TvShowId,
                Title = w.Episode.TvShow.Title,
                EpisodeTitle = w.Episode.Title,
                SeasonNumber = w.Episode.SeasonNumber,
                EpisodeNumber = w.Episode.EpisodeNumber,
                PosterPath = w.Episode.TvShow.PosterPath,
                BackdropPath = w.Episode.TvShow.BackdropPath,
                w.ProgressSeconds,
                RuntimeSeconds = (long?)(w.Episode.RuntimeMinutes * 60),
                w.LastWatched
            })
            .ToListAsync();

        var combined = movies.Cast<object>()
            .Concat(episodes.Cast<object>())
            .OrderByDescending(x => ((dynamic)x).LastWatched)
            .Take(20)
            .ToList();

        return Ok(combined);
    }
}
