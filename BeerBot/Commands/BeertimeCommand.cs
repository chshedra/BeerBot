using BeerBot.Data;
using BeerBot.Models;
using BeerBot.Resources;
using BeerBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramUser = Telegram.Bot.Types.User;
using User = BeerBot.Models.User;

namespace BeerBot.Commands;

// Invoked from a private chat: a registered group member runs /beertime to start a round.
// The initiator picks their own slots first, then everyone else is invited.
public class BeertimeCommand(
    BeerBotDbContext db,
    ITelegramBotClient bot,
    SlotWizard wizard,
    IConfiguration config,
    ILogger<BeertimeCommand> logger
)
{
    public async Task ExecuteAsync(Message message)
    {
        TelegramUser? from = message.From;

        if (from is null)
        {
            return;
        }

        long dmChatId = message.Chat.Id;

        User? user = await db.Users.FirstOrDefaultAsync(u => u.TelegramId == from.Id);

        if (user is null || user.GroupChatId == 0)
        {
            await bot.SendMessage(dmChatId, BotMessages.Beertime.NotLinked);
            return;
        }

        long groupChatId = user.GroupChatId;

        MeetingRequest? existing = await db.MeetingRequests.FirstOrDefaultAsync(r =>
            r.GroupChatId == groupChatId && r.Status == MeetingRequestStatus.Open
        );

        if (existing is not null)
        {
            await bot.SendMessage(dmChatId, BotMessages.Beertime.RoundInProgress);
            return;
        }

        int deadlineHours = config.GetValue("Bot:DeadlineHours", 24);
        MeetingRequest request = new()
        {
            GroupChatId = groupChatId,
            InitiatorUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            DeadlineAt = DateTime.UtcNow.AddHours(deadlineHours),
        };
        db.MeetingRequests.Add(request);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "MeetingRequest {Id} created for group {GroupChatId} by user {UserId}",
            request.Id,
            groupChatId,
            user.Id
        );

        // 1) The initiator picks their own availability right away.
        await wizard.SendDayPickerAsync(dmChatId, request.Id);

        // 2) DM every other already-registered member of this group.
        List<User> others = await db
            .Users.Where(u => u.GroupChatId == groupChatId && u.Id != user.Id)
            .ToListAsync();

        foreach (User other in others)
        {
            try
            {
                await wizard.SendDayPickerAsync(other.TelegramId, request.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to DM user {UserId} ({Name})",
                    other.TelegramId,
                    other.Name
                );
            }
        }

        // 3) Post a join button in the group so members who haven't started the bot can opt in.
        TelegramUser me = await bot.GetMe();
        string deepLink = $"https://t.me/{me.Username}?start={groupChatId}";
        InlineKeyboardMarkup keyboard = new(
            InlineKeyboardButton.WithUrl(BotMessages.Beertime.JoinButton, deepLink)
        );

        await bot.SendMessage(
            groupChatId,
            BotMessages.Beertime.GroupAnnouncement(user.Name, deadlineHours),
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard
        );
    }
}
