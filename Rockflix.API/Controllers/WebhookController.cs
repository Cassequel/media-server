using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController(AppDbContext db, ILogger<WebhookController> logger) : ControllerBase
{
    [HttpPost("radarr")]
    public async Task<IActionResult> Radarr([FromBody] RadarrEvent evt)
    {
        if (evt.EventType != "Download" || evt.Movie is null || evt.MovieFile is null)
            return Ok();

        var request = await db.MediaRequests
            .Where(r => r.MediaType == "movie" && r.ExternalId == evt.Movie.TmdbId && r.Status == "pending")
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync();

        if (request is not null)
        {
            request.FileSizeBytes = evt.MovieFile.Size;
            request.Status = "completed";
            await db.SaveChangesAsync();
            logger.LogInformation("Radarr: filled size {Size} bytes for request {Id}", evt.MovieFile.Size, request.Id);
        }

        return Ok();
    }

    [HttpPost("sonarr")]
    public async Task<IActionResult> Sonarr([FromBody] SonarrEvent evt)
    {
        if (evt.EventType != "Download" || evt.Series is null || evt.EpisodeFile is null)
            return Ok();

        var request = await db.MediaRequests
            .Where(r => r.MediaType == "tv" && r.ExternalId == evt.Series.TvdbId && r.Status == "pending")
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync();

        if (request is not null)
        {
            request.FileSizeBytes = (request.FileSizeBytes ?? 0) + evt.EpisodeFile.Size;
            request.Status = "completed";
            await db.SaveChangesAsync();
            logger.LogInformation("Sonarr: added {Size} bytes for request {Id}", evt.EpisodeFile.Size, request.Id);
        }

        return Ok();
    }

    public record RadarrEvent(
        string EventType,
        RadarrMovie? Movie,
        RadarrMovieFile? MovieFile);

    public record RadarrMovie(int TmdbId, string? Title);
    public record RadarrMovieFile(long Size);

    public record SonarrEvent(
        string EventType,
        SonarrSeries? Series,
        SonarrEpisodeFile? EpisodeFile);

    public record SonarrSeries(int TvdbId, string? Title);
    public record SonarrEpisodeFile(long Size);
}
