namespace Rockflix.API.DTOs.Media;

public class TvShowDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? Description { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public double? TmdbRating { get; set; }
    public string? Genre { get; set; }
    public int EpisodeCount { get; set; }
    public int SeasonCount { get; set; }
}

public class EpisodeDto
{
    public int Id { get; set; }
    public int TvShowId { get; set; }
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? StillPath { get; set; }
    public int? RuntimeMinutes { get; set; }
    public long? ProgressSeconds { get; set; }
    public bool? Completed { get; set; }
}
