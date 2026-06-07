using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        return StreamFile(movie.FilePath);
    }

    [HttpGet("episode/{id}")]
    public async Task<IActionResult> StreamEpisode(int id)
    {
        var episode = await db.Episodes.FindAsync(id);
        if (episode == null || !System.IO.File.Exists(episode.FilePath))
            return NotFound();

        return StreamFile(episode.FilePath);
    }

    private IActionResult StreamFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        // Non-MKV files: serve directly
        if (ext != ".mkv" && ext != ".avi" && ext != ".mov")
            return PhysicalFile(filePath, "video/mp4", enableRangeProcessing: true);

        // MKV/AVI/MOV: remux to MP4 via FFmpeg (no re-encoding, just repackaging)
        Response.ContentType = "video/mp4";
        Response.Headers.Append("Cache-Control", "no-cache");

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{filePath}\" -c:v copy -c:a aac -movflags frag_keyframe+empty_moov+faststart -f mp4 pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)!;
        HttpContext.RequestAborted.Register(() => { try { process.Kill(); } catch { } });

        return new FileStreamResult(process.StandardOutput.BaseStream, "video/mp4");
    }
}
