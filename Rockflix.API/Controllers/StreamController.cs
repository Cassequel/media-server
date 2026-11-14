using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rockflix.API.Data;
using Rockflix.API.Services;
using Microsoft.EntityFrameworkCore;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StreamController(AppDbContext db, StreamCacheService cache) : ControllerBase
{
    [HttpGet("movie/{id}")]
    public async Task<IActionResult> StreamMovie(int id, [FromQuery] int? versionId)
    {
        var versions = db.MovieVersions.Where(v => v.MovieId == id);
        var version = versionId.HasValue ? await versions.FirstOrDefaultAsync(v => v.Id == versionId)
            : await versions.OrderBy(v => v.RemoveSex || v.MuteLanguage).ThenBy(v => v.Id).FirstOrDefaultAsync();
        if (version == null || !System.IO.File.Exists(version.FilePath))
            return NotFound();

        return await StreamFile(version.FilePath);
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

        // A live FFmpeg pipe cannot answer HTTP Range requests, so native browser controls cannot
        // seek it. Materialize incompatible sources into the cache once, then always expose a
        // complete MP4 with range processing. This also prevents a new full transcode on every
        // retry or reload.
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var seekablePath = videoCompatible && audioCompatible && (ext == ".mp4" || ext == ".m4v")
            ? filePath
            : await cache.GetOrCreateSeekableMp4Async(filePath, videoCompatible, audioCompatible);

        return PhysicalFile(seekablePath, "video/mp4", enableRangeProcessing: true);
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
