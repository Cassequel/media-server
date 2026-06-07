namespace Rockflix.API.Models;

public class MediaRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string RequestText { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string? ResolvedTitle { get; set; }
    public int? ExternalId { get; set; } // tmdbId for movies, tvdbId for TV
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public long? FileSizeBytes { get; set; } // filled by Radarr/Sonarr webhook
    public string Status { get; set; } = "pending"; // pending | completed | failed
}
