using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;

namespace Rockflix.API.Services;

public class MediaRequestService
{
    private readonly AnthropicClient _claude;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MediaRequestService> _logger;
    private readonly IServiceScopeFactory _scopes;

    private static readonly string SystemPrompt = """
        You are a media request parser for a family-friendly media server. The user will send a natural language message requesting a movie or TV show.
        Extract their intent and return ONLY a JSON object — no prose, no markdown fences.

        IMPORTANT: If the request is for adult content, pornography, XXX material, NC-17 rated content, or anything sexually explicit,
        you MUST return {"blocked":true} and nothing else.

        JSON schema (for non-blocked requests):
        {
          "blocked": false,
          "media_type": "movie" | "tv",
          "title": "the best guess at the canonical title",
          "year": null | integer (release year if mentioned or strongly implied),
          "season": null | integer (season number for TV, null for movies or if unspecified),
          "confidence": "high" | "medium" | "low"
        }

        Examples:
        "that new Dune movie" -> {"blocked":false,"media_type":"movie","title":"Dune: Part Two","year":2024,"season":null,"confidence":"high"}
        "latest season of Severance" -> {"blocked":false,"media_type":"tv","title":"Severance","year":null,"season":2,"confidence":"medium"}
        "the bear" -> {"blocked":false,"media_type":"tv","title":"The Bear","year":null,"season":null,"confidence":"high"}
        """;

    public MediaRequestService(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<MediaRequestService> logger, IServiceScopeFactory scopes)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _scopes = scopes;
        _claude = new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = config["Anthropic:ApiKey"]! });
    }

    public async Task<ParsedLookup?> ParseAndLookupAsync(string requestText)
    {
        try
        {
            var parsed = await ParseWithClaudeAsync(requestText);
            if (parsed.Blocked) return new ParsedLookup("", "", null, null, null, Blocked: true);
            if (parsed.Confidence == "low") return null;

            // Do the Radarr/Sonarr lookup now so we lock in the exact external ID
            if (parsed.MediaType == "movie")
            {
                var http = _httpClientFactory.CreateClient("radarr");
                var lookup = await http.GetFromJsonAsync<RadarrMovie[]>(
                    $"/api/v3/movie/lookup?term={Uri.EscapeDataString(parsed.Title)}");
                if (lookup == null || lookup.Length == 0) return null;

                // Prefer exact year match, then closest title match
                var movie = parsed.Year.HasValue
                    ? lookup.FirstOrDefault(m => m.Year == parsed.Year.Value) ?? lookup[0]
                    : lookup[0];

                return new ParsedLookup(movie.Title, "movie", null, movie.TmdbId, null);
            }
            else
            {
                var http = _httpClientFactory.CreateClient("sonarr");
                var lookup = await http.GetFromJsonAsync<SonarrSeries[]>(
                    $"/api/v3/series/lookup?term={Uri.EscapeDataString(parsed.Title)}");
                if (lookup == null || lookup.Length == 0) return null;

                var series = parsed.Year.HasValue
                    ? lookup.FirstOrDefault(s => s.Year == parsed.Year.Value) ?? lookup[0]
                    : lookup[0];

                return new ParsedLookup(series.Title, "tv", parsed.Season, null, series.TvdbId);
            }
        }
        catch
        {
            return null;
        }
    }

    public async Task<MediaResult> ProcessConfirmedAsync(string title, string mediaType, int? season, int? tmdbId, int? tvdbId)
    {
        try
        {
            if (mediaType == "movie" && tmdbId.HasValue)
            {
                var existing = await CheckOriginalAsync(tmdbId.Value, title);
                if (existing != null) return existing;
                var http = _httpClientFactory.CreateClient("radarr");
                var managed = await FindRemoteAsync(http, "movie", "tmdbId", tmdbId.Value);
                if (managed != null) return new MediaResult("This movie is managed by Radarr, but a local original is not confirmed available. Check requests in Rockflix.", mediaType, title, null);
                var qualityProfileId = await ValidateProfileAsync(http, "Radarr");
                using var addResp = await http.PostAsJsonAsync("/api/v3/movie", new
                {
                    tmdbId = tmdbId.Value, title, qualityProfileId,
                    rootFolderPath = _config["Radarr:RootFolderPath"], monitored = true,
                    addOptions = new { searchForMovie = true }
                });
                if (!addResp.IsSuccessStatusCode)
                {
                    if ((int)addResp.StatusCode is 400 or 409 && await FindRemoteAsync(http, "movie", "tmdbId", tmdbId.Value) != null)
                        return new MediaResult("This movie is managed by Radarr, but a local original is not confirmed available. Check requests in Rockflix.", mediaType, title, null);
                    EnsureRemoteSuccess(addResp, "Radarr");
                }

                return new MediaResult($"Added '{title}' — download starting soon!", mediaType, title, tmdbId);
            }
            else if (mediaType == "tv" && tvdbId.HasValue)
            {
                var parsed = new MediaParsed(Blocked: false, MediaType: mediaType, Title: title, Year: null, Season: season, Confidence: "high");
                var (message, externalId) = await AddTvShowAsync(parsed, tvdbId);
                return new MediaResult(message, mediaType, title, externalId);
            }

            return new MediaResult($"Couldn't add '{title}'.", mediaType, title, null);
        }
        catch (RemoteRequestException ex) { return new MediaResult(ex.Message, mediaType, title, null); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add confirmed media: {Title}", title);
            return new MediaResult($"Found '{title}' but couldn't add it. Check that Radarr/Sonarr is running.", mediaType, title, null);
        }
    }

    public record ParsedLookup(string Title, string MediaType, int? Season, int? TmdbId, int? TvdbId, bool Blocked = false);

    private async Task<MediaResult?> CheckOriginalAsync(int tmdbId, string title)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var versions = await db.MovieVersions.Where(v => v.Movie.TmdbId == tmdbId).ToListAsync();
        if (versions.Any(v => !v.RemoveSex && !v.MuteLanguage && File.Exists(v.FilePath)))
            return new MediaResult($"The original of '{title}' is already available.", "movie", title, tmdbId, Existing: true);
        if (versions.Count > 0 || await db.MovieFilterJobs.AnyAsync(j => j.TmdbId == tmdbId))
            return new MediaResult($"'{title}' has version-specific requests. Request the original through Rockflix's download confirmation so existing versions are preserved.", "movie", title, null);
        return null;
    }

    public async Task<MediaResult> ProcessRequestAsync(string requestText)
    {
        MediaParsed? parsed;
        try
        {
            parsed = await ParseWithClaudeAsync(requestText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude parsing failed for: {Text}", requestText);
            return new MediaResult("Sorry, I couldn't understand that request. Try something like: 'add Dune Part Two' or 'download season 2 of Severance'.", "", null, null);
        }

        if (parsed.Confidence == "low")
            return new MediaResult($"I'm not sure what you're looking for. Did you mean '{parsed.Title}'? Reply with a more specific title.", parsed.MediaType, parsed.Title, null);

        try
        {
            if (parsed.Blocked) return new MediaResult("This title cannot be requested.", "", null, null);
            if (parsed.MediaType == "movie")
            {
                var http = _httpClientFactory.CreateClient("radarr");
                var found = await http.GetFromJsonAsync<RadarrMovie[]>($"/api/v3/movie/lookup?term={Uri.EscapeDataString(parsed.Title)}");
                var match = found?.FirstOrDefault(m => !parsed.Year.HasValue || m.Year == parsed.Year) ?? found?.FirstOrDefault();
                if (match != null) return await ProcessConfirmedAsync(match.Title, "movie", null, match.TmdbId, null);
            }
            var (message, externalId) = parsed.MediaType == "movie"
                ? await AddMovieAsync(parsed)
                : await AddTvShowAsync(parsed);
            return new MediaResult(message, parsed.MediaType, parsed.Title, externalId);
        }
        catch (RemoteRequestException ex) { return new MediaResult(ex.Message, parsed.MediaType, parsed.Title, null); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add media: {Title}", parsed.Title);
            return new MediaResult($"Found '{parsed.Title}' but couldn't add it. Check that Radarr/Sonarr is running.", parsed.MediaType, parsed.Title, null);
        }
    }

    private async Task<MediaParsed> ParseWithClaudeAsync(string smsText)
    {
        var response = await _claude.Messages.Create(new MessageCreateParams
        {
            Model = "claude-haiku-4-5-20251001",
            MaxTokens = 256,
            System = new List<TextBlockParam>
            {
                new() { Text = SystemPrompt, CacheControl = new CacheControlEphemeral() }
            },
            Messages = new List<MessageParam>
            {
                new UserMessageParam(smsText)
            }
        });

        string? json = null;
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                json = textBlock.Text;
                break;
            }
        }

        if (json is null)
            throw new InvalidOperationException("No text block in Claude response");

        // Strip markdown code fences if present
        json = json.Trim();
        if (json.StartsWith("```"))
        {
            json = json.TrimStart('`');
            if (json.StartsWith("json")) json = json[4..];
            json = json.TrimEnd('`').Trim();
        }

        return JsonSerializer.Deserialize<MediaParsed>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Claude response");
    }

    private async Task<(string message, int? externalId)> AddMovieAsync(MediaParsed parsed)
    {
        var http = _httpClientFactory.CreateClient("radarr");
        var lookup = await http.GetFromJsonAsync<RadarrMovie[]>($"/api/v3/movie/lookup?term={Uri.EscapeDataString(parsed.Title)}");
        var movie = lookup?.FirstOrDefault(m => !parsed.Year.HasValue || m.Year == parsed.Year) ?? lookup?.FirstOrDefault();
        if (movie == null) return ("Radarr found no matching movie.", null);
        var result = await ProcessConfirmedAsync(movie.Title, "movie", null, movie.TmdbId, null);
        return (result.Message, result.ExternalId);
    }

    private async Task<(string message, int? externalId)> AddTvShowAsync(MediaParsed parsed, int? confirmedId = null)
    {
        var http = _httpClientFactory.CreateClient("sonarr");
        if (confirmedId == null)
        {
            var matches = await http.GetFromJsonAsync<SonarrSeries[]>($"/api/v3/series/lookup?term={Uri.EscapeDataString(parsed.Title)}");
            confirmedId = (matches?.FirstOrDefault(s => !parsed.Year.HasValue || s.Year == parsed.Year) ?? matches?.FirstOrDefault())?.TvdbId;
        }
        if (confirmedId is null or <= 0) return ("Sonarr found no matching TV identity.", null);
        var existing = await FindRemoteAsync(http, "series", "tvdbId", confirmedId.Value);
        var lookup = existing == null
            ? await http.GetFromJsonAsync<JsonArray>($"/api/v3/series/lookup?term=tvdb:{confirmedId}") : null;
        var series = existing ?? lookup?.OfType<JsonObject>().FirstOrDefault(s => s["tvdbId"]?.GetValue<int>() == confirmedId);
        if (series == null) return ("Sonarr could not resolve the confirmed TVDB identity.", null);
        var seasons = series["seasons"]?.AsArray();
        var numbers = seasons?.OfType<JsonObject>().Select(s => s["seasonNumber"]!.GetValue<int>()).ToArray() ?? [];
        if (numbers.Length == 0) return ("Sonarr has no seasons for this series.", null);
        int selected = parsed.Season ?? numbers.Max();
        if (!numbers.Contains(selected)) return ("The requested season does not exist in Sonarr.", null);
        if (existing != null) await RequestExistingSeasonAsync(http, existing, selected);
        else
        {
            var profile = await ValidateProfileAsync(http, "Sonarr");
            series["qualityProfileId"] = profile;
            series["rootFolderPath"] = _config["Sonarr:RootFolderPath"];
            series["monitored"] = true;
            foreach (var season in seasons!.OfType<JsonObject>()) season["monitored"] = season["seasonNumber"]!.GetValue<int>() == selected;
            series["addOptions"] = new JsonObject { ["searchForMissingEpisodes"] = true };
            using var added = await http.PostAsJsonAsync("/api/v3/series", series);
            if (!added.IsSuccessStatusCode)
            {
                var raced = (int)added.StatusCode is 400 or 409 ? await FindRemoteAsync(http, "series", "tvdbId", confirmedId.Value) : null;
                if (raced == null) EnsureRemoteSuccess(added, "Sonarr");
                else await RequestExistingSeasonAsync(http, raced, selected);
            }
        }
        return ($"Requested '{parsed.Title}' Season {selected}. Sonarr search started.", confirmedId);
    }

    private static async Task RequestExistingSeasonAsync(HttpClient http, JsonObject series, int selected)
    {
        var season = series["seasons"]?.AsArray().OfType<JsonObject>().FirstOrDefault(s => s["seasonNumber"]?.GetValue<int>() == selected);
        if (season == null) throw new RemoteRequestException("The requested season does not exist in Sonarr.");
        series["monitored"] = true;
        season["monitored"] = true;
        int id = series["id"]!.GetValue<int>();
        using var updated = await http.PutAsJsonAsync($"/api/v3/series/{id}", series);
        EnsureRemoteSuccess(updated, "Sonarr");
        using var search = await http.PostAsJsonAsync("/api/v3/command", new { name = "SeasonSearch", seriesId = id, seasonNumber = selected });
        EnsureRemoteSuccess(search, "Sonarr");
    }

    private static async Task<JsonObject?> FindRemoteAsync(HttpClient http, string resource, string identity, int id)
    {
        using var response = await http.GetAsync($"/api/v3/{resource}?{identity}={id}");
        EnsureRemoteSuccess(response, resource == "movie" ? "Radarr" : "Sonarr");
        var items = await response.Content.ReadFromJsonAsync<JsonArray>();
        return items?.OfType<JsonObject>().FirstOrDefault(item => item[identity]?.GetValue<int>() == id);
    }

    private async Task<int> ValidateProfileAsync(HttpClient http, string service)
    {
        var raw = _config[$"{service}:QualityProfileId"] ?? "4";
        if (!int.TryParse(raw, out var id) || id <= 0) throw new RemoteRequestException($"{service} quality profile configuration is invalid.");
        using var response = await http.GetAsync("/api/v3/qualityprofile");
        EnsureRemoteSuccess(response, service);
        var profiles = await response.Content.ReadFromJsonAsync<JsonArray>();
        if (profiles?.OfType<JsonObject>().Any(p => p["id"]?.GetValue<int>() == id) != true)
            throw new RemoteRequestException($"{service} configured quality profile does not exist. Ask the administrator to select a valid profile.");
        return id;
    }

    private static void EnsureRemoteSuccess(HttpResponseMessage response, string service)
    {
        if (response.IsSuccessStatusCode) return;
        var message = (int)response.StatusCode switch
        {
            401 or 403 => $"{service} authentication failed. Ask the administrator to check its API credentials.",
            400 or 409 or 422 => $"{service} rejected the request. Check the quality profile, root folder and requested media settings.",
            _ => $"{service} is unavailable or returned an unexpected response. Please retry later."
        };
        throw new RemoteRequestException(message);
    }
    private sealed class RemoteRequestException(string message) : Exception(message);

    public record MediaResult(string Message, string MediaType, string? ResolvedTitle, int? ExternalId, bool Existing = false);

    public record MediaParsed(
        [property: JsonPropertyName("blocked")] bool Blocked = false,
        [property: JsonPropertyName("media_type")] string MediaType = "",
        [property: JsonPropertyName("title")] string Title = "",
        [property: JsonPropertyName("year")] int? Year = null,
        [property: JsonPropertyName("season")] int? Season = null,
        [property: JsonPropertyName("confidence")] string Confidence = "low");

    private record RadarrMovie(
        [property: JsonPropertyName("tmdbId")] int TmdbId,
        string Title,
        int Year);

    private record SonarrSeries(
        [property: JsonPropertyName("tvdbId")] int TvdbId,
        string Title,
        int Year,
        List<SonarrSeasonInfo>? Seasons);

    private record SonarrSeasonInfo(
        [property: JsonPropertyName("seasonNumber")] int SeasonNumber,
        bool Monitored);
}

