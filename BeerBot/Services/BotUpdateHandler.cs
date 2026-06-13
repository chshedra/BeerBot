using BeerBot.Commands;
using BeerBot.Data;
using BeerBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BeerBot.Services;

public class BotUpdateHandler(
    BeerBotDbContext db,
    ITelegramBotClient bot,
    OpenRouterClient gemini,
    BeertimeCommand beertime,
    StatusCommand status,
    SuggestCommand suggest,
    ILogger<BotUpdateHandler> logger
)
{
    public async Task HandleAsync(Update update)
    {
        logger.LogInformation("Update received: {Type}", update.Type);

        if (update.Type != UpdateType.Message || update.Message is not { } message)
            return;

        if (message.Chat.Type is ChatType.Group or ChatType.Supergroup)
        {
            await HandleGroupMessageAsync(message);
        }
        else if (message.Chat.Type == ChatType.Private)
        {
            await HandlePrivateMessageAsync(message);
        }
    }

    private async Task HandleGroupMessageAsync(Message message)
    {
        // Service message fired when members (possibly the bot itself) are added to the group.
        if (message.NewChatMembers is { Length: > 0 } newMembers)
        {
            var me = await bot.GetMe();
            if (newMembers.Any(m => m.Id == me.Id))
                await SendWelcomeAsync(message.Chat.Id);
            return;
        }

        var text = message.Text ?? string.Empty;
        var command = text.Split(' ', '@')[0].ToLowerInvariant();

        switch (command)
        {
            case "/beertime":
                await beertime.ExecuteAsync(message);
                break;
            case "/status":
                await status.ExecuteAsync(message);
                break;
            case "/suggest":
                await suggest.ExecuteAsync(message);
                break;
        }
    }

    private async Task SendWelcomeAsync(long groupChatId)
    {
        logger.LogInformation("Bot added to group {GroupChatId}; sending welcome", groupChatId);
        await bot.SendMessage(
            groupChatId,
            "🍺 *Я пивной бот!*\n\n"
                + "Я здесь чтобы помочь вамн айти время и бахнуть пивка.\n\n"
                + "Как это работает:\n"
                + "*Commands:*\n"
                + "/beertime — start a new round\n"
                + "/status — see who's replied\n"
                + "/suggest — post the suggestion now\n\n"
                + "Ready when you are — type /beertime to kick things off!",
            parseMode: ParseMode.Markdown
        );
    }

    private async Task HandlePrivateMessageAsync(Message message)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
            return;

        var from = message.From;
        if (from is null)
            return;

        var text = message.Text.Trim();

        // Deep-link entry: tapping the group's /beertime button sends "/start <groupChatId>".
        // This is the only way a bot can start a DM with — and learn — a group member.
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await HandleStartAsync(message, from, text);
            return;
        }

        await HandleAvailabilityReplyAsync(message, from);
    }

    private async Task HandleStartAsync(Message message, Telegram.Bot.Types.User from, string text)
    {
        // Extract the start payload: "/start -1001234567890" → group chat id
        long? groupId = null;
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && long.TryParse(parts[1], out var parsed))
            groupId = parsed;

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramId == from.Id);
        if (user is null)
        {
            user = new Models.User
            {
                TelegramId = from.Id,
                Name = $"{from.FirstName} {from.LastName}".Trim(),
                GroupChatId = groupId ?? 0,
            };
            db.Users.Add(user);
        }
        else if (groupId is not null)
        {
            user.GroupChatId = groupId.Value;
            user.Name = $"{from.FirstName} {from.LastName}".Trim();
        }
        await db.SaveChangesAsync();

        logger.LogInformation(
            "User {TelegramId} started bot; linked to group {GroupChatId}",
            from.Id,
            user.GroupChatId
        );

        if (user.GroupChatId == 0)
        {
            await bot.SendMessage(
                message.Chat.Id,
                "Hi! Open me from your group's /beertime button so I can link you to the group. 🍺"
            );
            return;
        }

        var request = await db.MeetingRequests.FirstOrDefaultAsync(r =>
            r.GroupChatId == user.GroupChatId && r.Status == MeetingRequestStatus.Open
        );

        if (request is null)
        {
            await bot.SendMessage(
                message.Chat.Id,
                "You're all set! 🍺 I'll message you here next time your group runs /beertime."
            );
            return;
        }

        var alreadyReplied = await db.Availabilities.AnyAsync(a =>
            a.RequestId == request.Id && a.UserId == user.Id
        );

        if (alreadyReplied)
        {
            await bot.SendMessage(
                message.Chat.Id,
                "Thanks, I already have your availability for this round! 🍺"
            );
            return;
        }

        await bot.SendMessage(
            message.Chat.Id,
            "Hey! When are you free this week? 🍺 Just reply here with your availability "
                + "(e.g. \"Monday evening, Wednesday after 6pm\")."
        );
    }

    private async Task HandleAvailabilityReplyAsync(Message message, Telegram.Bot.Types.User from)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramId == from.Id);

        if (user is null || user.GroupChatId == 0)
        {
            await bot.SendMessage(
                message.Chat.Id,
                "You're not linked to a group yet. Tap the 🍺 button under your group's /beertime message first!"
            );
            return;
        }

        var request = await db.MeetingRequests.FirstOrDefaultAsync(r =>
            r.GroupChatId == user.GroupChatId && r.Status == MeetingRequestStatus.Open
        );

        if (request is null)
        {
            await bot.SendMessage(
                message.Chat.Id,
                "There's no active beertime round in your group right now."
            );
            return;
        }

        var alreadyReplied = await db.Availabilities.AnyAsync(a =>
            a.RequestId == request.Id && a.UserId == user.Id
        );

        if (alreadyReplied)
        {
            await bot.SendMessage(
                message.Chat.Id,
                "Thanks, I already have your availability! I'll let you know when everyone's replied."
            );
            return;
        }

        var slots = await gemini.ParseAvailabilityAsync(message.Text!, DateTime.UtcNow);

        var availability = new Availability
        {
            UserId = user.Id,
            RequestId = request.Id,
            RawText = message.Text!,
            ParsedSlotsJson = slots,
        };
        db.Availabilities.Add(availability);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Availability saved for user {UserId} on request {RequestId} ({Count} slots)",
            user.Id,
            request.Id,
            slots.Count
        );

        await bot.SendMessage(
            message.Chat.Id,
            slots.Count > 0
                ? $"Got it! I found {slots.Count} time slot(s) from your message. I'll notify the group once everyone's replied. 🍺"
                : "I received your message but couldn't parse any specific times. Try again with something like \"Monday 6pm to 9pm\" or \"free Wednesday evening\"."
        );
    }
}
