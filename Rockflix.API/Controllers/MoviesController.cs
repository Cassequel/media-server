using System.Security.Claims;
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
public class MoviesController(AppDbContext db, TmdbService tmdb) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = UserId;
        var movies = await db.Movies
            .Select(m => new MovieDto
            {
                Id = m.Id,
                Title = m.Title,
                Year = m.Year,
                Description = m.Description,
                PosterPath = m.PosterPath,
                BackdropPath = m.BackdropPath,
                TmdbRating = m.TmdbRating,
                TmdbId = m.TmdbId,
                Genre = m.Genre,
                RuntimeMinutes = m.RuntimeMinutes,
                ProgressSeconds = db.WatchHistory
                    .Where(w => w.UserId == userId && w.MovieId == m.Id)
                    .Select(w => (long?)w.ProgressSeconds)
                    .FirstOrDefault(),
                Completed = db.WatchHistory
                    .Where(w => w.UserId == userId && w.MovieId == m.Id)
                    .Select(w => (bool?)w.Completed)
                    .FirstOrDefault()
            })
            .ToListAsync();

        await AddVersions(movies);
        return Ok(movies);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var userId = UserId;
        var movie = await db.Movies
            .Where(m => m.Id == id)
            .Select(m => new MovieDto
            {
                Id = m.Id,
                Title = m.Title,
                Year = m.Year,
                Description = m.Description,
                PosterPath = m.PosterPath,
                BackdropPath = m.BackdropPath,
                TmdbRating = m.TmdbRating,
                TmdbId = m.TmdbId,
                Genre = m.Genre,
                RuntimeMinutes = m.RuntimeMinutes,
                ProgressSeconds = db.WatchHistory
                    .Where(w => w.UserId == userId && w.MovieId == m.Id)
                    .Select(w => (long?)w.ProgressSeconds)
                    .FirstOrDefault(),
                Completed = db.WatchHistory
                    .Where(w => w.UserId == userId && w.MovieId == m.Id)
                    .Select(w => (bool?)w.Completed)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (movie == null) return NotFound();
        await AddVersions([movie]);
        return Ok(movie);
    }

    private async Task AddVersions(IReadOnlyCollection<MovieDto> movies)
    {
        // List.Contains avoids the .NET 10 span overload, which EF Core 9 cannot parameterize.
        var ids = movies.Select(m => m.Id).ToList();
        var versions = await db.MovieVersions.Where(v => ids.Contains(v.MovieId))
            .OrderBy(v => v.RemoveSex || v.MuteLanguage).ThenBy(v => v.Id)
            .Select(v => new { v.MovieId, Version = new MovieVersionDto(v.Id, v.RemoveSex, v.MuteLanguage, v.AudioLanguage, v.DurationSeconds, v.Notice,
                db.WatchHistory.Where(w => w.UserId == UserId && w.MovieVersionId == v.Id).Select(w => (long?)w.ProgressSeconds).FirstOrDefault(),
                db.WatchHistory.Where(w => w.UserId == UserId && w.MovieVersionId == v.Id).Select(w => (bool?)w.Completed).FirstOrDefault()) }).ToListAsync();
        var byMovie = versions.ToLookup(v => v.MovieId, v => v.Version);
        foreach (var movie in movies)
        {
            movie.Versions = byMovie[movie.Id].ToList();
            movie.DefaultVersionId = movie.Versions.FirstOrDefault()?.Id;
            movie.ProgressSeconds = movie.Versions.FirstOrDefault()?.ProgressSeconds;
            movie.Completed = movie.Versions.FirstOrDefault()?.Completed;
        }
    }

    [HttpGet("{id}/credits")]
    public async Task<IActionResult> GetCredits(int id)
    {
        var movie = await db.Movies.FindAsync(id);
        if (movie == null) return NotFound();
        if (movie.TmdbId == null) return Ok(new TmdbCreditsResult());
        var credits = await tmdb.GetCreditsAsync(movie.TmdbId.Value, isTv: false);
        return Ok(credits ?? new TmdbCreditsResult());
    }

    [HttpPost("{id}/progress")]
    public async Task<IActionResult> UpdateProgress(int id, [FromBody] ProgressRequest request)
    {
        var userId = UserId;
        var versions = db.MovieVersions.Where(v => v.MovieId == id);
        var version = request.VersionId.HasValue ? await versions.FirstOrDefaultAsync(v => v.Id == request.VersionId)
            : await versions.OrderBy(v => v.RemoveSex || v.MuteLanguage).ThenBy(v => v.Id).FirstOrDefaultAsync();
        if (version == null || request.ProgressSeconds < 0) return BadRequest();
        var entry = await db.WatchHistory
            .FirstOrDefaultAsync(w => w.UserId == userId && w.MovieVersionId == version.Id);

        if (entry == null)
        {
            db.WatchHistory.Add(new Models.WatchHistory
            {
                UserId = userId,
                MovieId = id,
                MovieVersionId = version.Id,
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
