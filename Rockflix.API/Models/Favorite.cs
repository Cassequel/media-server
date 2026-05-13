namespace Rockflix.API.Models;

public class Favorite
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? MovieId { get; set; }
    public int? TvShowId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public User User { get; set; } = null!;
    public Movie? Movie { get; set; }
    public TvShow? TvShow { get; set; }
}
