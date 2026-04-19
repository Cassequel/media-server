namespace Rockflix.API.Models;

public class TvShow
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
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Episode> Episodes { get; set; } = [];
}
