namespace BeerBot.Models;

/// <summary>
/// A group member known to the bot, linked to the group they registered from.
/// </summary>
public class User
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>The user's Telegram id (unique).</summary>
    public long TelegramId { get; set; }

    /// <summary>Display name shown in status and suggestion messages.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Telegram chat id of the group the user is linked to; 0 if not yet linked.</summary>
    public long GroupChatId { get; set; }
}
