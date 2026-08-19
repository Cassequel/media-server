using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Rockflix.API.Services;

/// <summary>
/// Guarantees a seekable MP4 on disk for source files whose video/audio codecs are already
/// browser-compatible but whose container isn't (e.g. H.264/AAC inside an MKV). The source is
/// remuxed (stream-copied, no re-encoding) into a cached MP4 the first time it's requested, so
/// later requests - and everyone else watching the same file - get instant, scrubbable playback.
/// </summary>
public class StreamCacheService
{
    private readonly string _cacheRoot;
    private readonly ILogger<StreamCacheService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public StreamCacheService(IConfiguration config, ILogger<StreamCacheService> logger)
    {
        var mediaRoot = config["Media:RootPath"]!;
        _cacheRoot = config["Media:CachePath"] ?? Path.Combine(mediaRoot, ".stream-cache");
        Directory.CreateDirectory(_cacheRoot);
        _logger = logger;
    }

    public async Task<string> GetOrCreateRemuxAsync(string sourcePath, CancellationToken ct)
    {
        var cachePath = GetCachePath(sourcePath);
        if (IsCacheValid(sourcePath, cachePath))
            return cachePath;

        var gate = _locks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Another request may have finished the remux while we were waiting on the gate.
            if (IsCacheValid(sourcePath, cachePath))
                return cachePath;

            var tmpPath = cachePath + ".tmp";
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{sourcePath}\" -c:v copy -c:a copy -movflags faststart -f mp4 \"{tmpPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            // Run the remux to completion even if this particular request aborts (e.g. the
            // caller navigates away), so a disconnect doesn't leave a half-written cache file
            // for the next viewer of this file to trip over - and so they get the benefit of it.
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None);
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(tmpPath))
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
                _logger.LogError("Remux failed for {Source}: {Stderr}", sourcePath, stderr);
                throw new InvalidOperationException($"Failed to remux '{sourcePath}' for seekable playback");
            }

            File.Move(tmpPath, cachePath, overwrite: true);
            return cachePath;
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetCachePath(string sourcePath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath)))[..16];
        return Path.Combine(_cacheRoot, $"{hash}.mp4");
    }

    private static bool IsCacheValid(string sourcePath, string cachePath) =>
        File.Exists(cachePath) && File.GetLastWriteTimeUtc(cachePath) >= File.GetLastWriteTimeUtc(sourcePath);
}
