using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;

namespace Rockflix.API.Services.Filtering;

public class DoesTheDogDieService(IHttpClientFactory clients, IConfiguration config, IServiceScopeFactory scopes)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime nextRequest = DateTime.MinValue;
    private static readonly HashSet<int> SexTopics = [279, 197, 292];
    private static readonly HashSet<int> LanguageTopics = [251, 272, 247, 314];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AssessmentResult> AssessAsync(int tmdbId, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(115));
        var ct = timeout.Token;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cached = await db.FilterAssessments.FindAsync([tmdbId], ct);
        if (cached != null && cached.FetchedAt > DateTime.UtcNow.AddHours(-12))
            return JsonSerializer.Deserialize<AssessmentResult>(cached.Json, Json)!;
        if (string.IsNullOrWhiteSpace(config["DoesTheDogDie:ApiKey"]))
            throw new InvalidOperationException("The timestamp source is not configured.");
        using var items = await GetAsync($"items?tmdb={tmdbId}", ct);
        var item = items.RootElement.EnumerateArray().FirstOrDefault(i => Int(i, "tmdbId") == tmdbId && Int(i, "itemTypeId") == 15);
        var intervals = new List<EditInterval>();
        string sourceUrl = "https://www.doesthedogdie.com";
        if (item.ValueKind != JsonValueKind.Undefined)
        {
            int id = Int(item, "id");
            sourceUrl += $"/media/{id}";
            intervals = await ReadIntervalsAsync(id, path => GetAsync(path, ct));
        }
        FilterCategory category(string action, string message)
        {
            int count = intervals.Count(i => i.Action == action);
            return new(count > 0 ? "limited" : "unavailable", count, count > 0 ? message : "No usable start/end timestamps are available for this filter.");
        }
        var result = new AssessmentResult(tmdbId,
            category("skip", "Sexual-content sources may be incomplete or broader than your selected preferences. " + EditPlan.UnverifiedNotice),
            category("mute", "The current source covers selected slurs, not comprehensive strong profanity. " + EditPlan.UnverifiedNotice),
            intervals, sourceUrl, DateTime.UtcNow);
        // Upsert: simultaneous assessments may safely share one stored result.
        string json = JsonSerializer.Serialize(result, Json);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FilterAssessments" ("TmdbId", "Json", "FetchedAt") VALUES ({tmdbId}, {json}, {result.FetchedAt})
            ON CONFLICT ("TmdbId") DO UPDATE SET "Json" = EXCLUDED."Json", "FetchedAt" = EXCLUDED."FetchedAt"
            """, ct);
        return result;
    }

    public static async Task<List<EditInterval>> ReadIntervalsAsync(int itemId, Func<string, Task<JsonDocument>> fetch)
    {
        using var ratings = await fetch($"items/{itemId}/ratings");
        if (ratings.RootElement.GetArrayLength() < 200) return ParseRatings(ratings.RootElement);
        // The live all-topic response can stop at 200 entries. Query relevant topics
        // individually rather than declaring missing timestamps from that partial page.
        var intervals = new List<EditInterval>();
        foreach (int topic in SexTopics.Concat(LanguageTopics).Order())
        {
            using var selected = await fetch($"items/{itemId}/ratings?topicId={topic}");
            intervals.AddRange(ParseRatings(selected.RootElement));
        }
        return intervals.Distinct().OrderBy(i => i.Start).Take(1000).ToList();
    }

    public static List<EditInterval> ParseRatings(JsonElement ratings)
    {
        var result = new List<EditInterval>();
        foreach (var r in ratings.EnumerateArray())
        {
            int topic = Int(r, "topicId");
            if (!SexTopics.Contains(topic) && !LanguageTopics.Contains(topic)) continue;
            if (Int(r, "yes") != 1 || Bool(r, "isRampant")) continue;
            double? start = Timestamp(r, "position"), end = Timestamp(r, "safePosition");
            if (start is null || end is null || end <= start || end - start > 1800) continue;
            result.Add(new(start.Value, end.Value, SexTopics.Contains(topic) ? "skip" : "mute", topic, Bool(r, "isSceneAlert")));
        }
        return result.Distinct().OrderBy(i => i.Start).Take(1000).ToList();
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (nextRequest > DateTime.UtcNow) await Task.Delay(nextRequest - DateTime.UtcNow, ct);
            nextRequest = DateTime.UtcNow.AddSeconds(2.1);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.doesthedogdie.com/api/v3/" + path);
            request.Headers.Add("X-API-KEY", config["DoesTheDogDie:ApiKey"]);
            using var response = await clients.CreateClient("doesthedogdie").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("The timestamp source has reached its request limit. Retry later or choose original.");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("The timestamp source is temporarily unavailable. Retry later or choose original.");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        finally { gate.Release(); }
    }
    private static int Int(JsonElement r, string key) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : -1;
    private static bool Bool(JsonElement r, string key) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static double? Timestamp(JsonElement r, string prefix)
    {
        int h = Int(r, prefix + "1"), m = Int(r, prefix + "2"), s = Int(r, prefix + "3");
        return h is >= 0 and < 12 && m is >= 0 and < 60 && s is >= 0 and < 60 ? h * 3600 + m * 60 + s : null;
    }
}
