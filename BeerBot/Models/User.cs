namespace BeerBot.Models;

public class User
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long GroupChatId { get; set; }
}
