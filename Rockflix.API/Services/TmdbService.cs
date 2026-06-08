using System.Text.Json;

namespace Rockflix.API.Services;

public class TmdbService(HttpClient http, IConfiguration config)
{
    private readonly string _apiKey = config["Tmdb:ApiKey"]!;
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string ImageBase = "https://image.tmdb.org/t/p/w500";

    public async Task<TmdbMovieResult?> SearchMovieAsync(string title, int? year)
    {
        var query = Uri.EscapeDataString(title);
        var yearParam = year.HasValue ? $"&year={year}" : "";
        var url = $"{BaseUrl}/search/movie?api_key={_apiKey}&query={query}{yearParam}";

        var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results");

        if (results.GetArrayLength() == 0) return null;

        // If year provided, prefer the result whose release year matches
        JsonElement best = results[0];
        if (year.HasValue)
        {
            for (int i = 0; i < results.GetArrayLength(); i++)
            {
                var item = results[i];
                if (item.TryGetProperty("release_date", out var rd) &&
                    rd.GetString() is string releaseDate &&
                    releaseDate.Length >= 4 &&
                    int.TryParse(releaseDate[..4], out int releaseYear) &&
                    releaseYear == year.Value)
                {
                    best = item;
                    break;
                }
            }
        }

        return new TmdbMovieResult
        {
            TmdbId = best.GetProperty("id").GetInt32(),
            Title = best.TryGetProperty("title", out var t) ? t.GetString() : title,
            Description = best.TryGetProperty("overview", out var o) ? o.GetString() : null,
            PosterPath = best.TryGetProperty("poster_path", out var p) && p.ValueKind != JsonValueKind.Null
                ? ImageBase + p.GetString() : null,
            BackdropPath = best.TryGetProperty("backdrop_path", out var b) && b.ValueKind != JsonValueKind.Null
                ? "https://image.tmdb.org/t/p/w1280" + b.GetString() : null,
            Rating = best.TryGetProperty("vote_average", out var r) ? r.GetDouble() : null,
            Genre = null
        };
    }

    public async Task<TmdbTvResult?> SearchTvShowAsync(string title, int? year)
    {
        var query = Uri.EscapeDataString(title);
        var yearParam = year.HasValue ? $"&first_air_date_year={year}" : "";
        var url = $"{BaseUrl}/search/tv?api_key={_apiKey}&query={query}{yearParam}";

        var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results");

        if (results.GetArrayLength() == 0) return null;

        var first = results[0];
        return new TmdbTvResult
        {
            TmdbId = first.GetProperty("id").GetInt32(),
            Title = first.TryGetProperty("name", out var t) ? t.GetString() : title,
            Description = first.TryGetProperty("overview", out var o) ? o.GetString() : null,
            PosterPath = first.TryGetProperty("poster_path", out var p) && p.ValueKind != JsonValueKind.Null
                ? ImageBase + p.GetString() : null,
            BackdropPath = first.TryGetProperty("backdrop_path", out var b) && b.ValueKind != JsonValueKind.Null
                ? "https://image.tmdb.org/t/p/w1280" + b.GetString() : null,
            Rating = first.TryGetProperty("vote_average", out var r) ? r.GetDouble() : null
        };
    }

    public async Task<TmdbEpisodeResult?> GetEpisodeAsync(int tmdbId, int season, int episode)
    {
        var url = $"{BaseUrl}/tv/{tmdbId}/season/{season}/episode/{episode}?api_key={_apiKey}";
        var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new TmdbEpisodeResult
        {
            Title = root.TryGetProperty("name", out var t) ? t.GetString() ?? $"Episode {episode}" : $"Episode {episode}",
            Description = root.TryGetProperty("overview", out var o) ? o.GetString() : null,
            StillPath = root.TryGetProperty("still_path", out var s) && s.ValueKind != JsonValueKind.Null
                ? ImageBase + s.GetString() : null,
            RuntimeMinutes = root.TryGetProperty("runtime", out var r) && r.ValueKind != JsonValueKind.Null
                ? r.GetInt32() : null
        };
    }
}

public record TmdbMovieResult
{
    public int TmdbId { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? PosterPath { get; init; }
    public string? BackdropPath { get; init; }
    public double? Rating { get; init; }
    public string? Genre { get; init; }
}

public record TmdbTvResult
{
    public int TmdbId { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? PosterPath { get; init; }
    public string? BackdropPath { get; init; }
    public double? Rating { get; init; }
}

public record TmdbEpisodeResult
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? StillPath { get; init; }
    public int? RuntimeMinutes { get; init; }
}
