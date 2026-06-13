using BeerBot.Data;
using BeerBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BeerBot.Commands;

public class BeertimeCommand(
    BeerBotDbContext db,
    ITelegramBotClient bot,
    IConfiguration config,
    ILogger<BeertimeCommand> logger
)
{
    private const string AvailabilityPrompt = "Когда свободен на неделе, чтобы бахнуть пивка? 🍺 ";

    public async Task ExecuteAsync(Message message)
    {
        long groupChatId = message.Chat.Id;

        MeetingRequest? existing = await db.MeetingRequests.FirstOrDefaultAsync(r =>
            r.GroupChatId == groupChatId && r.Status == MeetingRequestStatus.Open
        );

        if (existing is not null)
        {
            await bot.SendMessage(
                groupChatId,
                "Так, уже же вроде собираемся! Напиши /status чтобы посмотреть кто тормозит."
            );
            return;
        }

        int deadlineHours = config.GetValue("Bot:DeadlineHours", 24);
        var request = new MeetingRequest
        {
            GroupChatId = groupChatId,
            CreatedAt = DateTime.UtcNow,
            DeadlineAt = DateTime.UtcNow.AddHours(deadlineHours),
        };
        db.MeetingRequests.Add(request);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "MeetingRequest {Id} created for group {GroupChatId}",
            request.Id,
            groupChatId
        );

        // Telegram bots can't DM members who haven't started a chat with them, nor list
        // group members. So we post a deep-link button: tapping it opens a private chat and
        // sends "/start <groupChatId>", which registers the member and links them to this group.
        Telegram.Bot.Types.User me = await bot.GetMe();
        string? deepLink = $"https://t.me/{me.Username}?start={groupChatId}";
        InlineKeyboardMarkup keyboard = new(
            InlineKeyboardButton.WithUrl("🍺 Когда бы пивка глотнул?", deepLink)
        );

        await bot.SendMessage(
            groupChatId,
            "🍺 *Beertime!* Tap the button below to DM me your availability for this week. "
                + $"I'll find the best slot and post a poll here once everyone's replied (or in {deadlineHours}h).",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard
        );

        // Returning members who already linked to this group in a previous round can be
        // DM'd directly — no need for them to tap the button again.
        List<Models.User> users = await db
            .Users.Where(u => u.GroupChatId == groupChatId)
            .ToListAsync();

        foreach (var user in users)
        {
            try
            {
                await bot.SendMessage(user.TelegramId, AvailabilityPrompt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to DM user {UserId} ({Name})",
                    user.TelegramId,
                    user.Name
                );
            }
        }
    }
}
