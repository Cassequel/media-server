using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StreamController(AppDbContext db) : ControllerBase
{
    [HttpGet("movie/{id}")]
    public async Task<IActionResult> StreamMovie(int id)
    {
        var movie = await db.Movies.FindAsync(id);
        if (movie == null || !System.IO.File.Exists(movie.FilePath))
            return NotFound();

        return PhysicalFile(movie.FilePath, "video/mp4", enableRangeProcessing: true);
    }

    [HttpGet("episode/{id}")]
    public async Task<IActionResult> StreamEpisode(int id)
    {
        var episode = await db.Episodes.FindAsync(id);
        if (episode == null || !System.IO.File.Exists(episode.FilePath))
            return NotFound();

        return PhysicalFile(episode.FilePath, "video/mp4", enableRangeProcessing: true);
    }
}
