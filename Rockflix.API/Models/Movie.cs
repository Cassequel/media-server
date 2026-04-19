namespace Rockflix.API.Models;

public class Movie
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? Description { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public double? TmdbRating { get; set; }
    public int? TmdbId { get; set; }
    public string? Genre { get; set; }
    public int? RuntimeMinutes { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WatchHistory> WatchHistory { get; set; } = [];
}
