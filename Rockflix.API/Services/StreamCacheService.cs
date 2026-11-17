using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Rockflix.API.Services.Filtering;

namespace Rockflix.API.Services;

/// <summary>
/// Guarantees a seekable, browser-compatible MP4 on disk. Sources which are already H.264/AAC
/// are remuxed; other sources are transcoded once. Later requests - and everyone else watching
/// the same file - get instant, scrubbable playback with HTTP range requests.
/// </summary>
public class StreamCacheService
{
    private const long DefaultMaxCacheBytes = 20L * 1024 * 1024 * 1024; // 20 GiB

    private readonly EncodingOptions _encoding;
    private readonly string _cacheRoot;
    private readonly long _maxCacheBytes;
    private readonly long _freeSpaceFloorBytes;
    private readonly ILogger<StreamCacheService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    // One encoder prevents several first-play requests from exhausting the host.
    private readonly SemaphoreSlim _conversionGate = MediaEncodingGate.Semaphore;

    public StreamCacheService(IConfiguration config, ILogger<StreamCacheService> logger, IOptions<EncodingOptions>? encoding = null)
    {
        _encoding = encoding?.Value ?? new EncodingOptions();
        _encoding.Validate();
        var mediaRoot = config["Media:RootPath"]!;
        _cacheRoot = config["Media:CachePath"] ?? Path.Combine(mediaRoot, ".stream-cache");
        _maxCacheBytes = config.GetValue<long?>("Media:CacheMaxBytes") ?? DefaultMaxCacheBytes;
        _freeSpaceFloorBytes = config.GetValue<long?>("Media:FreeSpaceFloorBytes") ?? 2L * 1024 * 1024 * 1024;
        Directory.CreateDirectory(_cacheRoot);
        _logger = logger;
    }

    public async Task<string> GetOrCreateSeekableMp4Async(string sourcePath, bool copyVideo, bool copyAudio)
    {
        var cachePath = GetCachePath(sourcePath);
        if (IsCacheValid(sourcePath, cachePath))
        {
            Touch(cachePath);
            return cachePath;
        }

        var gate = _locks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await _conversionGate.WaitAsync();
        await gate.WaitAsync();
        try
        {
            // Another request may have finished the conversion while we were waiting on the gate.
            if (IsCacheValid(sourcePath, cachePath))
            {
                Touch(cachePath);
                return cachePath;
            }

            var tmpPath = cachePath + ".tmp";
            long reservation = new FileInfo(sourcePath).Length * (copyVideo ? 2 : 3);
            if (reservation > _maxCacheBytes) throw new InvalidOperationException("This conversion exceeds the playback cache budget.");
            EvictLeastRecentlyUsed(keep: cachePath, reservedBytes: reservation);
            if (new DriveInfo(_cacheRoot).AvailableFreeSpace < reservation + _freeSpaceFloorBytes)
                throw new InvalidOperationException("Not enough free disk space for playback conversion.");
            var videoArgs = copyVideo ? "-c:v copy" : "-c:v libx264 -preset veryfast -crf 20";
            var audioArgs = copyAudio ? "-c:a copy" : "-c:a aac";
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in new[] { "-nostdin", "-v", "error", "-y" }.Concat(_encoding.InputArguments()).Concat(new[] { "-i", sourcePath })) psi.ArgumentList.Add(arg);
            // Match the first video/audio streams inspected by StreamController.
            foreach (var arg in new[] { "-map", "0:v:0", "-map", "0:a:0?" }) psi.ArgumentList.Add(arg);
            foreach (var arg in (videoArgs + " " + audioArgs).Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(arg);
            foreach (var arg in _encoding.OutputArguments()) psi.ArgumentList.Add(arg);
            foreach (var arg in new[] { "-movflags", "+faststart", "-f", "mp4", tmpPath }) psi.ArgumentList.Add(arg);

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
                _logger.LogError("Seekable conversion failed for {Source}: {Stderr}", sourcePath, stderr);
                throw new InvalidOperationException($"Failed to convert '{sourcePath}' for seekable playback");
            }

            File.Move(tmpPath, cachePath, overwrite: true);
            Touch(cachePath);
            EvictLeastRecentlyUsed(keep: cachePath);
            return cachePath;
        }
        finally
        {
            gate.Release();
            _conversionGate.Release();
        }
    }

    // Keeps the cache directory under the configured size cap by deleting the least-recently-
    // played cached files first. Runs after every new remux, since that's the only way the cache
    // grows. Deleting a file that's mid-stream to some other viewer is safe on Linux - the open
    // file handle keeps working until they finish, it just won't be reused after that.
    private void EvictLeastRecentlyUsed(string keep, long reservedBytes = 0)
    {
        try
        {
            var files = new DirectoryInfo(_cacheRoot).EnumerateFiles("*.mp4").OrderBy(f => f.LastAccessTimeUtc).ToList();
            var total = files.Sum(f => f.Length);

            foreach (var file in files)
            {
                if (total <= _maxCacheBytes - reservedBytes) break;
                if (file.FullName == keep) continue;
                total -= file.Length;
                try
                {
                    file.Delete();
                    _logger.LogInformation("Evicted cached remux {File} to stay under the cache size limit", file.Name);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not evict {File}, will retry next time", file.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache eviction pass failed");
        }
    }

    private static void Touch(string path)
    {
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { /* best-effort LRU bookkeeping */ }
    }

    private string GetCachePath(string sourcePath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath)))[..16];
        return Path.Combine(_cacheRoot, $"{hash}.mp4");
    }

    private static bool IsCacheValid(string sourcePath, string cachePath) =>
        File.Exists(cachePath) && File.GetLastWriteTimeUtc(cachePath) >= File.GetLastWriteTimeUtc(sourcePath);
}
