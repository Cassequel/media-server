using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rockflix.API.Services.Filtering;

public record MediaProbe(double Duration, string VideoCodec, int AudioIndex, string AudioLanguage, List<int> TextSubtitles, bool UnsupportedSubtitles);
public record ProcessedMedia(double Duration, long Size, string Fingerprint, string Notice);

public class MediaProcessor
{
    private readonly EncodingOptions _encoding;

    public MediaProcessor(IOptions<EncodingOptions>? encoding = null)
    {
        _encoding = encoding?.Value ?? new EncodingOptions();
        _encoding.Validate();
    }
    public static async Task<string> RunAsync(string executable, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start media processing.");
        using var registration = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        string stderr = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} failed: {stderr[..Math.Min(stderr.Length, 1200)]}");
        return await output;
    }

    public async Task<MediaProbe> ProbeAsync(string path, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await RunAsync("ffprobe", ["-v", "error", "-show_streams", "-show_format", "-of", "json", path], ct));
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var video = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
        var audio = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "audio");
        if (video.ValueKind == JsonValueKind.Undefined || audio.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("This file must have video and audio tracks.");
        string language = audio.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var lang) ? lang.GetString() ?? "unknown" : "unknown";
        var subtitles = streams.Where(s => s.GetProperty("codec_type").GetString() == "subtitle").ToList();
        string[] textCodecs = ["subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text"];
        var supported = subtitles.Where(s => textCodecs.Contains(s.GetProperty("codec_name").GetString())).Select(s => s.GetProperty("index").GetInt32()).ToList();
        return new(double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            video.GetProperty("codec_name").GetString() ?? "", audio.GetProperty("index").GetInt32(), language, supported, supported.Count != subtitles.Count);
    }

    public static async Task<string> FingerprintAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    public async Task<ProcessedMedia> ProcessAsync(string source, string output, IReadOnlyList<EditInterval> edits, bool muteLanguage, CancellationToken ct)
    {
        await MediaEncodingGate.Semaphore.WaitAsync(ct);
        try { return await ProcessCoreAsync(source, output, edits, muteLanguage, ct); }
        finally { MediaEncodingGate.Semaphore.Release(); }
    }

    private async Task<ProcessedMedia> ProcessCoreAsync(string source, string output, IReadOnlyList<EditInterval> edits, bool muteLanguage, CancellationToken ct)
    {
        var probe = await ProbeAsync(source, ct);
        if (muteLanguage && probe.AudioLanguage is not ("eng" or "en"))
            throw new InvalidOperationException("Language filtering currently requires a primary audio track tagged English. Choose original or remove language filtering.");
        if (edits.Count == 0) throw new InvalidOperationException("No usable edits exist for the requested filters.");
        if (edits.Any(i => i.End > probe.Duration || i.Start < 0 || i.End <= i.Start))
            throw new InvalidOperationException("Timestamp boundaries do not match this file. Choose original or retry with another source.");
        var keep = EditPlan.KeepRanges(probe.Duration, edits);
        var fingerprint = await FingerprintAsync(source, ct);
        var args = new List<string> { "-nostdin", "-hide_banner", "-v", "error", "-y" };
        args.AddRange(_encoding.InputArguments());
        args.AddRange(["-i", source]);
        // Extract text tracks first, then apply exactly the same timeline transformation as the media.
        var subFiles = new List<string>();
        foreach (int index in probe.TextSubtitles)
        {
            var raw = await RunAsync("ffmpeg", ["-nostdin", "-v", "error", .._encoding.InputArguments(), "-i", source, "-map", $"0:{index}", .._encoding.OutputArguments(), "-f", "srt", "-"], ct);
            string srt = SubtitleEdits.Apply(raw, keep, muteLanguage);
            if (string.IsNullOrWhiteSpace(srt)) continue;
            string sub = output + $".sub{index}.srt";
            await File.WriteAllTextAsync(sub, srt, ct);
            subFiles.Add(sub);
            args.AddRange(["-i", sub]);
        }
        bool cut = edits.Any(e => e.Action == "skip");
        string audioFilter = EditPlan.AudioFilter(edits);
        string graphPath = output + ".filters";
        if (cut)
        {
            var graph = new StringBuilder();
            for (int i = 0; i < keep.Count; i++)
            {
                var k = keep[i];
                graph.Append($"[0:v:0]trim=start={EditPlan.Number(k.Start)}:end={EditPlan.Number(k.End)},setpts=PTS-STARTPTS[v{i}];");
                graph.Append($"[0:{probe.AudioIndex}]{audioFilter},atrim=start={EditPlan.Number(k.Start)}:end={EditPlan.Number(k.End)},asetpts=PTS-STARTPTS[a{i}];");
            }
            for (int i = 0; i < keep.Count; i++) graph.Append($"[v{i}][a{i}]");
            graph.Append($"concat=n={keep.Count}:v=1:a=1[vout][aout]");
            await File.WriteAllTextAsync(graphPath, graph.ToString(), ct);
            args.AddRange(["-filter_complex_script", graphPath, "-map", "[vout]", "-map", "[aout]"]);
        }
        else args.AddRange(["-map", "0:v:0", "-map", $"0:{probe.AudioIndex}", "-af", audioFilter]);
        for (int i = 0; i < subFiles.Count; i++) args.AddRange(["-map", $"{i + 1}:s:0"]);
        args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1", "-c:v", !cut && probe.VideoCodec == "h264" ? "copy" : "libx264"]);
        if (cut || probe.VideoCodec != "h264") args.AddRange(["-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p"]);
        args.AddRange(_encoding.OutputArguments());
        args.AddRange(["-c:a", "aac", "-c:s", "mov_text", "-metadata:s:a:0", $"language={probe.AudioLanguage}", "-movflags", "+faststart", "-f", "mp4", output]);
        await RunAsync("ffmpeg", args, ct);
        var result = await ValidateCoreAsync(output, keep.Sum(k => k.End - k.Start), ct);
        if (await FingerprintAsync(source, ct) != fingerprint) throw new InvalidOperationException("The source changed during filtering; output was not published.");
        foreach (var sub in subFiles) File.Delete(sub);
        if (File.Exists(graphPath)) File.Delete(graphPath);
        return new(result.Duration, new FileInfo(output).Length, fingerprint,
            "Only the primary audio track is included. " + (probe.UnsupportedSubtitles ? "Unsupported image subtitles were omitted. " : "") +
            "Text burned into the picture cannot be muted or separately filtered.");
    }

    public async Task<MediaProbe> ValidateAsync(string path, double expectedDuration, CancellationToken ct)
    {
        await MediaEncodingGate.Semaphore.WaitAsync(ct);
        try { return await ValidateCoreAsync(path, expectedDuration, ct); }
        finally { MediaEncodingGate.Semaphore.Release(); }
    }

    // ProcessAsync already owns the gate through encoding and full validation.
    private async Task<MediaProbe> ValidateCoreAsync(string path, double expectedDuration, CancellationToken ct)
    {
        var probe = await ProbeAsync(path, ct);
        if (Math.Abs(probe.Duration - expectedDuration) > 1.0) throw new InvalidOperationException("Output duration did not match the edit plan.");
        await RunAsync("ffmpeg", ["-nostdin", "-v", "error", "-xerror", .._encoding.InputArguments(), "-i", path, "-map", "0:v:0", "-map", "0:a:0", .._encoding.OutputArguments(), "-f", "null", "-"], ct);
        return probe;
    }
}

public static class SubtitleEdits
{
    private static readonly Regex Profanity = new(@"\b(?:fuck\w*|motherfuck\w*|shit\w*|bullshit|cunt\w*|cock(?:sucker)?s?|dicks?|puss(?:y|ies)|nigg(?:er|a)s?|fagg?ots?|retard(?:ed|s)?|trann(?:y|ies))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string Apply(string srt, IReadOnlyList<EditInterval> keep, bool mute)
    {
        var output = new StringBuilder();
        int index = 1;
        foreach (var block in Regex.Split(srt.Replace("\r", ""), @"\n\s*\n"))
        {
            var lines = block.Trim().Split('\n');
            int t = Array.FindIndex(lines, l => l.Contains(" --> "));
            if (t < 0) continue;
            var match = Regex.Match(lines[t], @"(\d+:\d+:\d+[,.]\d+)\s+-->\s+(\d+:\d+:\d+[,.]\d+)");
            if (!match.Success) continue;
            double start = Parse(match.Groups[1].Value), end = Parse(match.Groups[2].Value);
            string text = string.Join("\n", lines.Skip(t + 1));
            if (mute) text = Profanity.Replace(text, "[muted]");
            double offset = 0;
            foreach (var k in keep)
            {
                double a = Math.Max(start, k.Start), b = Math.Min(end, k.End);
                if (b > a) output.Append($"{index++}\n{Format(offset + a - k.Start)} --> {Format(offset + b - k.Start)}\n{text}\n\n");
                offset += k.End - k.Start;
            }
        }
        return output.ToString();
    }
    private static double Parse(string t) => TimeSpan.Parse(t.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture).TotalSeconds;
    private static string Format(double s) => TimeSpan.FromSeconds(s).ToString(@"hh\:mm\:ss\,fff", System.Globalization.CultureInfo.InvariantCulture);
}
