using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FavoritesController(AppDbContext db) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var favorites = await db.Favorites
            .Where(f => f.UserId == UserId)
            .OrderByDescending(f => f.AddedAt)
            .Select(f => new
            {
                f.Id,
                f.MovieId,
                f.TvShowId,
                f.AddedAt,
                Title = f.MovieId != null ? f.Movie!.Title : f.TvShow!.Title,
                PosterPath = f.MovieId != null ? f.Movie!.PosterPath : f.TvShow!.PosterPath,
                BackdropPath = f.MovieId != null ? f.Movie!.BackdropPath : f.TvShow!.BackdropPath,
                TmdbRating = f.MovieId != null ? f.Movie!.TmdbRating : f.TvShow!.TmdbRating,
                Year = f.MovieId != null ? f.Movie!.Year : f.TvShow!.Year,
                Type = f.MovieId != null ? "movie" : "tvshow"
            })
            .ToListAsync();

        return Ok(favorites);
    }

    [HttpPost("movie/{movieId}")]
    public async Task<IActionResult> ToggleMovie(int movieId)
    {
        var userId = UserId;
        var existing = await db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.MovieId == movieId);
        if (existing != null)
        {
            db.Favorites.Remove(existing);
            await db.SaveChangesAsync();
            return Ok(new { favorited = false });
        }

        db.Favorites.Add(new Favorite { UserId = userId, MovieId = movieId, AddedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return Ok(new { favorited = true });
    }

    [HttpPost("tvshow/{tvShowId}")]
    public async Task<IActionResult> ToggleTvShow(int tvShowId)
    {
        var userId = UserId;
        var existing = await db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.TvShowId == tvShowId);
        if (existing != null)
        {
            db.Favorites.Remove(existing);
            await db.SaveChangesAsync();
            return Ok(new { favorited = false });
        }

        db.Favorites.Add(new Favorite { UserId = userId, TvShowId = tvShowId, AddedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return Ok(new { favorited = true });
    }

    [HttpGet("movie/{movieId}/status")]
    public async Task<IActionResult> MovieStatus(int movieId)
    {
        var favorited = await db.Favorites.AnyAsync(f => f.UserId == UserId && f.MovieId == movieId);
        return Ok(new { favorited });
    }

    [HttpGet("tvshow/{tvShowId}/status")]
    public async Task<IActionResult> TvShowStatus(int tvShowId)
    {
        var favorited = await db.Favorites.AnyAsync(f => f.UserId == UserId && f.TvShowId == tvShowId);
        return Ok(new { favorited });
    }
}
