namespace Rockflix.API.Models;

public class WatchHistory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? MovieId { get; set; }
    public int? EpisodeId { get; set; }
    public long ProgressSeconds { get; set; } = 0;
    public bool Completed { get; set; } = false;
    public DateTime LastWatched { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Movie? Movie { get; set; }
    public Episode? Episode { get; set; }
}
