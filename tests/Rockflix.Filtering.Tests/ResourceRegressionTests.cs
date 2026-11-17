using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Rockflix.API.Services;
using Rockflix.API.Services.Filtering;

// Invoked by Program.cs; --media enables synthetic FFmpeg regression fixtures.
// Run serially: these tests intentionally exercise the process-wide encoding semaphore.
public static class ResourceRegressionTests
{
    public static async Task RunAsync(Action<bool, string> check, bool includeMedia = false)
    {
        var defaults = new EncodingOptions();
        check(defaults.CodecThreads == 2 && defaults.FilterThreads == 1, "conservative encoding defaults");
        foreach (var invalid in new[] {
            new EncodingOptions { CodecThreads = 0 }, new EncodingOptions { CodecThreads = -1 },
            new EncodingOptions { FilterThreads = 0 }, new EncodingOptions { FilterThreads = -1 } })
        {
            bool rejected = false;
            try { new MediaProcessor(Options.Create(invalid)); }
            catch (ValidationException) { rejected = true; }
            check(rejected, "invalid thread count rejected before processing");
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Encoding:CodecThreads"] = "3", ["Encoding:FilterThreads"] = "2"
        }).Build();
        var services = new ServiceCollection();
        services.AddOptions<EncodingOptions>().Bind(config.GetSection(EncodingOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EncodingOptions>>().Value;
        check(options.InputArguments().SequenceEqual(new[] { "-threads", "3", "-filter_threads", "2", "-filter_complex_threads", "2" })
            && options.OutputArguments().SequenceEqual(new[] { "-threads", "3" }), "configuration binds decoder, encoder and both filter controls");

        var processor = new MediaProcessor();
        await MediaEncodingGate.Semaphore.WaitAsync();
        try
        {
            using var cancel = new CancellationTokenSource();
            var waiting = processor.ValidateAsync("must-not-be-opened", 1, cancel.Token);
            check(!waiting.IsCompleted, "standalone validation waits for conversion gate before probing");
            cancel.Cancel();
            bool canceled = false;
            try { await waiting; } catch (OperationCanceledException) { canceled = true; }
            check(canceled && MediaEncodingGate.Semaphore.CurrentCount == 0, "canceled waiter does not release another owner's gate");
        }
        finally { MediaEncodingGate.Semaphore.Release(); }

        if (!includeMedia) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var root = Path.Combine(Path.GetTempPath(), "rockflix-resource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.mp4");
            await MediaProcessor.RunAsync("ffmpeg", ["-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=24",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "3", "-c:v", "libx264", "-threads", "2",
                "-c:a", "aac", "-metadata:s:a:0", "language=eng", source], ct);
            // Concurrent callers must complete; ProcessAsync internally validates without reacquiring.
            var processing = processor.ProcessAsync(source, Path.Combine(root, "cut.mp4"), [new(1, 2, "skip")], false, ct);
            var validation = processor.ValidateAsync(source, 3, ct);
            await Task.WhenAll(processing, validation);
            check(Math.Abs(processing.Result.Duration - 2) <= 1 && MediaEncodingGate.Semaphore.CurrentCount == 1,
                "conversion and standalone full validation complete and release shared gate");
            var muted = Path.Combine(root, "mute.mp4");
            await processor.ProcessAsync(source, muted, [new(1, 2, "mute")], true, ct);
            check((await processor.ProbeAsync(muted, ct)).VideoCodec == "h264", "mute-only copy-video branch remains valid");
            bool rejected = false;
            try { await processor.ValidateAsync(source, 100, ct); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && MediaEncodingGate.Semaphore.CurrentCount == 1, "validation failure releases gate");

            var multi = Path.Combine(root, "multiple.mp4");
            await MediaProcessor.RunAsync("ffmpeg", ["-nostdin", "-v", "error", "-y",
                "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=24",
                "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
                "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000",
                "-map", "0:v", "-map", "1:v", "-map", "2:a", "-map", "3:a",
                "-t", "1", "-c:v", "libx264", "-threads", "2", "-c:a", "aac",
                "-disposition:v:0", "0", "-disposition:v:1", "default",
                "-disposition:a:0", "0", "-disposition:a:1", "default",
                "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=fra", multi], ct);
            foreach (bool copy in new[] { true, false })
            {
                var cacheConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                    ["Media:RootPath"] = root, ["Media:CachePath"] = Path.Combine(root, "cache-" + copy),
                    ["Media:FreeSpaceFloorBytes"] = "0"
                }).Build();
                var cache = new StreamCacheService(cacheConfig, NullLogger<StreamCacheService>.Instance);
                var cached = await cache.GetOrCreateSeekableMp4Async(multi, copy, copy).WaitAsync(ct);
                using var streams = JsonDocument.Parse(await MediaProcessor.RunAsync("ffprobe",
                    ["-v", "error", "-show_streams", "-of", "json", cached], ct));
                var tracks = streams.RootElement.GetProperty("streams").EnumerateArray().ToList();
                var video = tracks.Single(t => t.GetProperty("codec_type").GetString() == "video");
                var audio = tracks.Single(t => t.GetProperty("codec_type").GetString() == "audio");
                check(tracks.Count == 2 && video.GetProperty("width").GetInt32() == 160 && audio.GetProperty("channels").GetInt32() == 1
                    && audio.GetProperty("tags").GetProperty("language").GetString() == "eng",
                    $"playback copy={copy} maps first tracks despite later defaults and preserves language metadata");
                var silent = Path.Combine(root, $"silent-{copy}.mp4");
                await MediaProcessor.RunAsync("ffmpeg", ["-nostdin", "-v", "error", "-y", "-i", source,
                    "-map", "0:v:0", "-c:v", "copy", "-an", silent], ct);
                var silentCached = await cache.GetOrCreateSeekableMp4Async(silent, copy, copy).WaitAsync(ct);
                using var silentStreams = JsonDocument.Parse(await MediaProcessor.RunAsync("ffprobe",
                    ["-v", "error", "-show_streams", "-of", "json", silentCached], ct));
                check(silentStreams.RootElement.GetProperty("streams").GetArrayLength() == 1,
                    $"playback copy={copy} accepts video without audio");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
