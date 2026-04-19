namespace Rockflix.API.Models;

public class Episode
{
    public int Id { get; set; }
    public int TvShowId { get; set; }
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? StillPath { get; set; }
    public int? RuntimeMinutes { get; set; }
    public string FilePath { get; set; } = string.Empty;

    public TvShow TvShow { get; set; } = null!;
    public ICollection<WatchHistory> WatchHistory { get; set; } = [];
}
