namespace Rockflix.API.Models;

public class TelegramUser
{
    public int Id { get; set; }
    public long ChatId { get; set; }
    public string? DisplayName { get; set; }
    public DateTime AuthorizedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public List<TelegramRequest> Requests { get; set; } = [];
}
