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
public class MoviesController(AppDbContext db) : ControllerBase
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
        return Ok(movie);
    }

    [HttpPost("{id}/progress")]
    public async Task<IActionResult> UpdateProgress(int id, [FromBody] ProgressRequest request)
    {
        var userId = UserId;
        var entry = await db.WatchHistory
            .FirstOrDefaultAsync(w => w.UserId == userId && w.MovieId == id);

        if (entry == null)
        {
            db.WatchHistory.Add(new Models.WatchHistory
            {
                UserId = userId,
                MovieId = id,
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
