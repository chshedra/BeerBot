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
    SlotWizard wizard,
    BeertimeCommand beertime,
    StatusCommand status,
    SuggestCommand suggest,
    ILogger<BotUpdateHandler> logger
)
{
    public async Task HandleAsync(Update update)
    {
        logger.LogInformation("Update received: {Type}", update.Type);

        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is { } callback)
        {
            await HandleCallbackAsync(callback);
            return;
        }

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

    private async Task HandleCallbackAsync(CallbackQuery callback)
    {
        var submittedRequestId = await wizard.HandleCallbackAsync(callback);
        if (submittedRequestId is null)
            return;

        // A user just finished — see if that completes the round.
        var request = await db.MeetingRequests.FirstOrDefaultAsync(r => r.Id == submittedRequestId);
        if (request is { Status: MeetingRequestStatus.Open })
            await suggest.PostIfReadyAsync(request, deadlinePassed: false);
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
                await bot.SendMessage(
                    message.Chat.Id,
                    "Напиши мне /beertime в личку, чтобы начать раунд 🍺"
                );
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
        var me = await bot.GetMe();
        string deepLink = $"https://t.me/{me.Username}?start={groupChatId}";

        await bot.SendMessage(
            groupChatId,
            "🍺 *Я пивной бот!*\n\n"
                + "Помогаю найти время и бахнуть пивка.\n\n"
                + "Как это работает:\n"
                + "1. Каждый жмёт кнопку ниже и стартует меня в личке.\n"
                + "2. Любой пишет мне /beertime в личку, чтобы начать раунд.\n"
                + "3. Все выбирают своё время кнопками — я нахожу пересечение и кидаю опрос сюда.\n\n"
                + $"Регистрация: [нажми тут]({deepLink})",
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

        // Deep-link entry: tapping the group's register/join button sends "/start <groupChatId>".
        // This is the only way a bot can start a DM with — and learn — a group member.
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await HandleStartAsync(message, from, text);
            return;
        }

        if (text.StartsWith("/beertime", StringComparison.OrdinalIgnoreCase))
        {
            await beertime.ExecuteAsync(message);
            return;
        }

        // Slots are entered with buttons now — nudge free-text users.
        await bot.SendMessage(
            message.Chat.Id,
            "Время выбирается кнопками 🍺 Напиши /beertime, чтобы начать раунд, "
                + "или жди приглашения, когда его начнёт кто-то из группы."
        );
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
                "Привет! Открой меня по кнопке из группы, чтобы я привязал тебя к ней. 🍺"
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
                "Готово! 🍺 Напиши /beertime, чтобы начать раунд, или жди приглашения от группы."
            );
            return;
        }

        var alreadySubmitted = await db.Availabilities.AnyAsync(a =>
            a.RequestId == request.Id && a.UserId == user.Id && a.Submitted
        );

        if (alreadySubmitted)
        {
            await bot.SendMessage(
                message.Chat.Id,
                "Спасибо, твоё время уже записано на этот раунд! 🍺"
            );
            return;
        }

        // Active round running — drop them straight into the wizard.
        await wizard.SendDayPickerAsync(message.Chat.Id, request.Id);
    }
}
