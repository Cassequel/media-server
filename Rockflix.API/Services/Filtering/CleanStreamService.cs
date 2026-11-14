using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Rockflix.API.Services.Filtering;

// Opt-in, title-allowlisted pilot. No scraping, bulk import, or external contributions.
public sealed class CleanStreamService(IHttpClientFactory clients, IConfiguration config) : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 100 });
    public bool Allowed(int tmdbId) => (config["CleanStream:AllowedTmdbIds"] ?? "").Split(',')
        .Any(v => int.TryParse(v.Trim(), out int id) && id == tmdbId);

    public async Task<AssessmentResult?> AssessAsync(int tmdbId, CancellationToken ct)
    {
        if (!Allowed(tmdbId)) return null;
        if (cache.TryGetValue(tmdbId, out AssessmentResult? cached)) return cached;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var identityRequest = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.themoviedb.org/3/movie/{tmdbId}/external_ids?api_key={Uri.EscapeDataString(config["Tmdb:ApiKey"] ?? "")}");
        using var identityResponse = await clients.CreateClient("timestamp-identity").SendAsync(identityRequest, deadline.Token);
        identityResponse.EnsureSuccessStatusCode();
        using var identity = JsonDocument.Parse(await identityResponse.Content.ReadAsStringAsync(deadline.Token));
        if (!identity.RootElement.TryGetProperty("id", out var id) || id.GetInt32() != tmdbId)
            throw new InvalidOperationException("Timestamp movie identity did not match.");
        string? imdb = identity.RootElement.TryGetProperty("imdb_id", out var value) ? value.GetString() : null;
        if (imdb == null || !Regex.IsMatch(imdb, @"^tt\d{7,10}$")) return null;
        string url = "https://cleanstream.elfhosted.com/api/filters/" + imdb;
        using var response = await clients.CreateClient("cleanstream").GetAsync(url, deadline.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
        var intervals = Parse(document.RootElement, imdb);
        var result = new AssessmentResult(tmdbId,
            new(intervals.Count > 0 ? "limited" : "unavailable", intervals.Count,
                "CleanStream / VideoSkip community timestamps; edition alignment and completeness are unverified. " + EditPlan.UnverifiedNotice),
            new("unavailable", 0, "This pilot source does not supply strong-language filtering."),
            intervals, url, DateTime.UtcNow);
        cache.Set(tmdbId, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12) });
        return result;
    }

    public static List<EditInterval> Parse(JsonElement document, string imdb)
    {
        if (String(document, "imdbId") != imdb || String(document, "type") != "movie")
            throw new InvalidOperationException("Community timestamps describe another movie or media type.");
        if (!document.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() > 1000)
            throw new InvalidOperationException("Invalid community timestamp response.");
        var result = new List<EditInterval>();
        foreach (var segment in segments.EnumerateArray())
        {
            string category = String(segment, "category");
            // All nudity is in scope. Generic low/medium 'sex' may mean romance/innuendo.
            if (category != "nudity" && !(category == "sex" && String(segment, "severity") == "high")) continue;
            if (!Number(segment, "startMs", out double start) || !Number(segment, "endMs", out double end)
                || start < 0 || end <= start || end > 12 * 3600_000 || end - start > 1800_000) continue;
            result.Add(new(start / 1000, end / 1000, "skip", SourceName: "CleanStream / VideoSkip",
                SourceUrl: "https://cleanstream.elfhosted.com/api/filters/" + imdb,
                SourceSegmentId: String(segment, "id")[..Math.Min(128, String(segment, "id").Length)]));
        }
        return result.DistinctBy(i => (i.Start, i.End, i.Action)).OrderBy(i => i.Start).ToList();
    }
    private static string String(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Number(JsonElement value, string key, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out number) && double.IsFinite(number);
    }
    public void Dispose() => cache.Dispose();
}
