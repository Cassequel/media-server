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

    public async Task<string> ProcessRequestAsync(string smsText)
    {
        MediaParsed? parsed;
        try
        {
            parsed = await ParseWithClaudeAsync(smsText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude parsing failed for: {Text}", smsText);
            return "Sorry, I couldn't understand that request. Try something like: 'add Dune Part Two' or 'download season 2 of Severance'.";
        }

        if (parsed.Confidence == "low")
            return $"I'm not sure what you're looking for. Did you mean '{parsed.Title}'? Reply with a more specific title.";

        try
        {
            return parsed.MediaType == "movie"
                ? await AddMovieAsync(parsed)
                : await AddTvShowAsync(parsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add media: {Title}", parsed.Title);
            return $"Found '{parsed.Title}' but couldn't add it. Check that Radarr/Sonarr is running.";
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

        return JsonSerializer.Deserialize<MediaParsed>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Claude response");
    }

    private async Task<string> AddMovieAsync(MediaParsed parsed)
    {
        var http = _httpClientFactory.CreateClient("radarr");
        var rootFolder = _config["Radarr:RootFolderPath"]!;
        var qualityProfileId = int.Parse(_config["Radarr:QualityProfileId"]!);

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

        if (!addResp.IsSuccessStatusCode && addResp.StatusCode != System.Net.HttpStatusCode.BadRequest)
            throw new HttpRequestException($"Radarr returned {addResp.StatusCode}");

        return addResp.StatusCode == System.Net.HttpStatusCode.BadRequest
            ? $"'{movie.Title}' is already in your library."
            : $"Added '{movie.Title}' ({movie.Year}) — download starting soon!";
    }

    private async Task<string> AddTvShowAsync(MediaParsed parsed)
    {
        var http = _httpClientFactory.CreateClient("sonarr");
        var rootFolder = _config["Sonarr:RootFolderPath"]!;
        var qualityProfileId = int.Parse(_config["Sonarr:QualityProfileId"]!);

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

        if (!addResp.IsSuccessStatusCode && addResp.StatusCode != System.Net.HttpStatusCode.BadRequest)
            throw new HttpRequestException($"Sonarr returned {addResp.StatusCode}");

        var seasonMsg = parsed.Season.HasValue ? $" Season {parsed.Season}" : "";
        return addResp.StatusCode == System.Net.HttpStatusCode.BadRequest
            ? $"'{series.Title}'{seasonMsg} is already in your library."
            : $"Added '{series.Title}'{seasonMsg} — download starting soon!";
    }

    private record MediaParsed(
        [property: JsonPropertyName("media_type")] string MediaType,
        string Title,
        int? Year,
        int? Season,
        string Confidence);

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
