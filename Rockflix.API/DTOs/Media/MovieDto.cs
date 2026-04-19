namespace Rockflix.API.DTOs.Media;

public class MovieDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? Description { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public double? TmdbRating { get; set; }
    public string? Genre { get; set; }
    public int? RuntimeMinutes { get; set; }
    public long? ProgressSeconds { get; set; }
    public bool? Completed { get; set; }
}
