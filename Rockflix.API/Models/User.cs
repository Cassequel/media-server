namespace Rockflix.API.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public long? TelegramChatId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WatchHistory> WatchHistory { get; set; } = [];
    public ICollection<MediaRequest> Requests { get; set; } = [];
}
