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

        return await StreamFile(movie.FilePath);
    }

    [HttpGet("episode/{id}")]
    public async Task<IActionResult> StreamEpisode(int id)
    {
        var episode = await db.Episodes.FindAsync(id);
        if (episode == null || !System.IO.File.Exists(episode.FilePath))
            return NotFound();

        return await StreamFile(episode.FilePath);
    }

    private static readonly HashSet<string> BrowserCompatibleVideoCodecs = ["h264"];
    private static readonly HashSet<string> BrowserCompatibleAudioCodecs = ["aac"];

    private async Task<IActionResult> StreamFile(string filePath)
    {
        var videoCodec = await ProbeCodecAsync(filePath, "v:0");
        var audioCodec = await ProbeCodecAsync(filePath, "a:0");
        var videoCompatible = videoCodec != null && BrowserCompatibleVideoCodecs.Contains(videoCodec);
        var audioCompatible = audioCodec != null && BrowserCompatibleAudioCodecs.Contains(audioCodec);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var containerCompatible = ext == ".mp4" || ext == ".m4v";

        // Already a phone-compatible MP4 (H.264 + AAC): serve the bytes directly so range
        // requests/seeking work and we don't spend CPU re-encoding for nothing.
        if (videoCompatible && audioCompatible && containerCompatible)
            return PhysicalFile(filePath, "video/mp4", enableRangeProcessing: true);

        // Otherwise remux via FFmpeg, copying whichever stream is already compatible and
        // re-encoding whichever isn't (e.g. HEVC video or DTS/EAC3/TrueHD audio) so phone
        // browsers - which generally only decode H.264/AAC inline - can actually play it.
        Response.ContentType = "video/mp4";
        Response.Headers.Append("Cache-Control", "no-cache");

        var videoArgs = videoCompatible ? "-c:v copy" : "-c:v libx264 -preset veryfast -crf 20";
        var audioArgs = audioCompatible ? "-c:a copy" : "-c:a aac";

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{filePath}\" {videoArgs} {audioArgs} -movflags frag_keyframe+empty_moov+faststart -f mp4 pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)!;
        HttpContext.RequestAborted.Register(() => { try { process.Kill(); } catch { } });

        return new FileStreamResult(process.StandardOutput.BaseStream, "video/mp4");
    }

    private static async Task<string?> ProbeCodecAsync(string filePath, string streamSelector)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -select_streams {streamSelector} -show_entries stream=codec_name -of default=nw=1:nk=1 \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        var codec = output.Trim().ToLowerInvariant();
        return codec.Length > 0 ? codec : null;
    }
}
