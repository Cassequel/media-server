using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Messages;

namespace Rockflix.API.Services;

public class MediaRequestService
{
    private readonly AnthropicClient _claude;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MediaRequestService> _logger;

    private static readonly string SystemPrompt = """
        You are a media request parser. The user will send a natural language SMS requesting a movie or TV show.
        Extract their intent and return ONLY a JSON object — no prose, no markdown fences.

        JSON schema:
        {
          "media_type": "movie" | "tv",
          "title": "the best guess at the canonical title",
          "year": null | integer (release year if mentioned or strongly implied),
          "season": null | integer (season number for TV, null for movies or if unspecified),
          "confidence": "high" | "medium" | "low"
        }

        Examples:
        "that new Dune movie" -> {"media_type":"movie","title":"Dune: Part Two","year":2024,"season":null,"confidence":"high"}
        "latest season of Severance" -> {"media_type":"tv","title":"Severance","year":null,"season":2,"confidence":"medium"}
        "the bear" -> {"media_type":"tv","title":"The Bear","year":null,"season":null,"confidence":"high"}
        """;

    public MediaRequestService(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<MediaRequestService> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _claude = new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = config["Anthropic:ApiKey"]! });
    }

    public async Task<MediaParsed?> ParseRequestAsync(string requestText)
    {
        try
        {
            var parsed = await ParseWithClaudeAsync(requestText);
            return parsed.Confidence == "low" ? null : parsed;
        }
        catch
        {
            return null;
        }
    }

    public async Task<MediaResult> ProcessParsedAsync(string title, string mediaType, int? season)
    {
        var parsed = new MediaParsed(mediaType, title, null, season, "high");
        try
        {
            var (message, externalId) = mediaType == "movie"
                ? await AddMovieAsync(parsed)
                : await AddTvShowAsync(parsed);
            return new MediaResult(message, mediaType, title, externalId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add confirmed media: {Title}", title);
            return new MediaResult($"Found '{title}' but couldn't add it. Check that Radarr/Sonarr is running.", mediaType, title, null);
        }
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
            var (message, externalId) = parsed.MediaType == "movie"
                ? await AddMovieAsync(parsed)
                : await AddTvShowAsync(parsed);
            return new MediaResult(message, parsed.MediaType, parsed.Title, externalId);
        }
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
                new() { Role = Role.User, Content = smsText }
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
        var rootFolder = _config["Radarr:RootFolderPath"]!;
        const int qualityProfileId = 4;

        var lookup = await http.GetFromJsonAsync<RadarrMovie[]>(
            $"/api/v3/movie/lookup?term={Uri.EscapeDataString(parsed.Title)}");
        var movie = lookup?.FirstOrDefault()
            ?? throw new InvalidOperationException($"Radarr found no results for '{parsed.Title}'");

        var addResp = await http.PostAsJsonAsync("/api/v3/movie", new
        {
            tmdbId = movie.TmdbId,
            title = movie.Title,
            year = movie.Year,
            qualityProfileId,
            rootFolderPath = rootFolder,
            monitored = true,
            addOptions = new { searchForMovie = true }
        });

        if (addResp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await addResp.Content.ReadAsStringAsync();
            _logger.LogWarning("Radarr 400 for {Title}: {Body}", movie.Title, body);
            if (!body.Contains("already")) throw new HttpRequestException($"Radarr rejected: {body}");
        }
        else if (!addResp.IsSuccessStatusCode)
            throw new HttpRequestException($"Radarr returned {addResp.StatusCode}");

        var message = addResp.StatusCode == System.Net.HttpStatusCode.BadRequest
            ? $"'{movie.Title}' is already in your library."
            : $"Added '{movie.Title}' ({movie.Year}) — download starting soon!";
        return (message, movie.TmdbId);
    }

    private async Task<(string message, int? externalId)> AddTvShowAsync(MediaParsed parsed)
    {
        var http = _httpClientFactory.CreateClient("sonarr");
        var rootFolder = _config["Sonarr:RootFolderPath"]!;
        const int qualityProfileId = 4;

        var lookup = await http.GetFromJsonAsync<SonarrSeries[]>(
            $"/api/v3/series/lookup?term={Uri.EscapeDataString(parsed.Title)}");
        var series = lookup?.FirstOrDefault()
            ?? throw new InvalidOperationException($"Sonarr found no results for '{parsed.Title}'");

        var seasons = series.Seasons?.Select(s => new
        {
            seasonNumber = s.SeasonNumber,
            monitored = parsed.Season.HasValue
                ? s.SeasonNumber == parsed.Season.Value
                : s.SeasonNumber == series.Seasons.Max(x => x.SeasonNumber)
        }).ToList<object>();

        var addResp = await http.PostAsJsonAsync("/api/v3/series", new
        {
            tvdbId = series.TvdbId,
            title = series.Title,
            qualityProfileId,
            rootFolderPath = rootFolder,
            monitored = true,
            seasons,
            addOptions = new { searchForMissingEpisodes = true }
        });

        if (addResp.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await addResp.Content.ReadAsStringAsync();
            _logger.LogWarning("Sonarr 400 for {Title}: {Body}", series.Title, body);
            if (!body.Contains("already")) throw new HttpRequestException($"Sonarr rejected: {body}");
        }
        else if (!addResp.IsSuccessStatusCode)
            throw new HttpRequestException($"Sonarr returned {addResp.StatusCode}");

        var seasonMsg = parsed.Season.HasValue ? $" Season {parsed.Season}" : "";
        var message = addResp.StatusCode == System.Net.HttpStatusCode.BadRequest
            ? $"'{series.Title}'{seasonMsg} is already in your library."
            : $"Added '{series.Title}'{seasonMsg} — download starting soon!";
        return (message, series.TvdbId);
    }

    public record MediaResult(string Message, string MediaType, string? ResolvedTitle, int? ExternalId);

    public record MediaParsed(
        [property: JsonPropertyName("media_type")] string MediaType,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("year")] int? Year,
        [property: JsonPropertyName("season")] int? Season,
        [property: JsonPropertyName("confidence")] string Confidence);

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
