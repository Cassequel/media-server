namespace Rockflix.API.Models;

public class TelegramRequest
{
    public int Id { get; set; }
    public long ChatId { get; set; }
    public string RequestText { get; set; } = string.Empty;
    public string? ResolvedTitle { get; set; }
    public string? MediaType { get; set; } // "movie" or "tv"
    public bool Success { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public TelegramUser? User { get; set; }
}
