using System.Globalization;
using BeerBot.Data;
using BeerBot.Models;
using BeerBot.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using User = BeerBot.Models.User;

namespace BeerBot.Services;

// Drives the inline-button availability wizard: pick a day, multi-select hourly slots,
// repeat for other days, then Done. State lives entirely in the DB (AvailabilitySlot rows)
// and in the rendered keyboard, so a restart never strands a half-finished user.
public class SlotWizard(BeerBotDbContext db, ITelegramBotClient bot, ILogger<SlotWizard> logger)
{
    private const int DaysToOffer = 7;
    private const int FirstHour = 12;
    private const int LastHour = 23; // last button is 23:00–00:00

    // Callback-data prefixes (kept short — Telegram caps callback data at 64 bytes).
    private const string DayPrefix = "d"; // d:{requestId}:{yyyyMMdd}  -> open hour picker
    private const string HourPrefix = "h"; // h:{requestId}:{yyyyMMdd}:{HH} -> toggle slot
    private const string BackPrefix = "b"; // b:{requestId} -> back to day picker
    private const string DonePrefix = "x"; // x:{requestId} -> submit
    private const string DateFormat = "yyyyMMdd";

    public async Task SendDayPickerAsync(long chatId, int requestId)
    {
        await bot.SendMessage(
            chatId,
            BotMessages.Wizard.PickDay,
            replyMarkup: BuildDayKeyboard(requestId)
        );
    }

    // Returns the requestId if the user just submitted (pressed Done), else null.
    public async Task<int?> HandleCallbackAsync(CallbackQuery query)
    {
        string data = query.Data ?? string.Empty;
        string[] parts = data.Split(':');
        string prefix = parts[0];

        if (query.Message is not { } message || query.From is null)
        {
            await Answer(query);
            return null;
        }

        if (!int.TryParse(parts.ElementAtOrDefault(1), out int requestId))
        {
            await Answer(query);
            return null;
        }

        MeetingRequest? request = await db.MeetingRequests.FirstOrDefaultAsync(r =>
            r.Id == requestId
        );

        if (request is null || request.Status != MeetingRequestStatus.Open)
        {
            await Answer(query, BotMessages.Wizard.RoundClosed);
            return null;
        }

        User? user = await db.Users.FirstOrDefaultAsync(u => u.TelegramId == query.From.Id);
        if (user is null)
        {
            await Answer(query);
            return null;
        }

        switch (prefix)
        {
            case DayPrefix:
                await ShowHourPickerAsync(message, requestId, user.Id, parts[2]);
                await Answer(query);
                return null;

            case HourPrefix:
                await ToggleHourAsync(message, request, user.Id, parts[2], parts[3]);
                await Answer(query);
                return null;

            case BackPrefix:
                await bot.EditMessageText(
                    message.Chat.Id,
                    message.MessageId,
                    BotMessages.Wizard.PickDay,
                    replyMarkup: BuildDayKeyboard(requestId)
                );
                await Answer(query);
                return null;

            case DonePrefix:
                return await SubmitAsync(query, message, request, user.Id);

            default:
                await Answer(query);
                return null;
        }
    }

    private async Task ShowHourPickerAsync(
        Message message,
        int requestId,
        int userId,
        string dateToken
    )
    {
        DateOnly date = ParseDate(dateToken);
        HashSet<int> selected = await GetSelectedHoursAsync(requestId, userId, date);

        await bot.EditMessageText(
            message.Chat.Id,
            message.MessageId,
            BotMessages.Wizard.PickHours(FormatDay(date)),
            replyMarkup: BuildHourKeyboard(requestId, date, selected)
        );
    }

    private async Task ToggleHourAsync(
        Message message,
        MeetingRequest request,
        int userId,
        string dateToken,
        string hourToken
    )
    {
        DateOnly date = ParseDate(dateToken);
        if (!int.TryParse(hourToken, out int hour))
        {
            return;
        }

        Availability availability = await GetOrCreateAvailabilityAsync(request.Id, userId);
        TimeOnly start = new(hour, 0);

        AvailabilitySlot? existing = await db.AvailabilitySlots.FirstOrDefaultAsync(s =>
            s.AvailabilityId == availability.Id && s.Date == date && s.Start == start
        );

        if (existing is not null)
        {
            db.AvailabilitySlots.Remove(existing);
        }
        else
        {
            db.AvailabilitySlots.Add(
                new AvailabilitySlot
                {
                    AvailabilityId = availability.Id,
                    Date = date,
                    Start = start,
                    End = start.AddHours(1),
                }
            );
        }
        await db.SaveChangesAsync();

        HashSet<int> selected = await GetSelectedHoursAsync(request.Id, userId, date);
        await bot.EditMessageReplyMarkup(
            message.Chat.Id,
            message.MessageId,
            replyMarkup: BuildHourKeyboard(request.Id, date, selected)
        );
    }

    private async Task<int?> SubmitAsync(
        CallbackQuery query,
        Message message,
        MeetingRequest request,
        int userId
    )
    {
        Availability? availability = await db
            .Availabilities.Include(a => a.Slots)
            .FirstOrDefaultAsync(a => a.RequestId == request.Id && a.UserId == userId);

        if (availability is null || availability.Slots.Count == 0)
        {
            await Answer(query, BotMessages.Wizard.SelectAtLeastOne);
            return null;
        }

        availability.Submitted = true;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "User {UserId} submitted {Count} slots for request {RequestId}",
            userId,
            availability.Slots.Count,
            request.Id
        );

        await bot.EditMessageText(
            message.Chat.Id,
            message.MessageId,
            BotMessages.Wizard.Submitted(availability.Slots.Count)
        );
        await Answer(query, BotMessages.Wizard.Saved);
        return request.Id;
    }

    private async Task<Availability> GetOrCreateAvailabilityAsync(int requestId, int userId)
    {
        Availability? availability = await db.Availabilities.FirstOrDefaultAsync(a =>
            a.RequestId == requestId && a.UserId == userId
        );

        if (availability is null)
        {
            availability = new Availability { RequestId = requestId, UserId = userId };
            db.Availabilities.Add(availability);
            await db.SaveChangesAsync();
        }

        return availability;
    }

    private async Task<HashSet<int>> GetSelectedHoursAsync(int requestId, int userId, DateOnly date)
    {
        List<int> hours = await db
            .AvailabilitySlots.Where(s =>
                s.Availability.RequestId == requestId
                && s.Availability.UserId == userId
                && s.Date == date
            )
            .Select(s => s.Start.Hour)
            .ToListAsync();

        return hours.ToHashSet();
    }

    private static InlineKeyboardMarkup BuildDayKeyboard(int requestId)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        List<InlineKeyboardButton[]> rows = [];

        for (int i = 0; i < DaysToOffer; i++)
        {
            DateOnly date = today.AddDays(i);
            rows.Add([
                InlineKeyboardButton.WithCallbackData(
                    FormatDay(date),
                    $"{DayPrefix}:{requestId}:{date.ToString(DateFormat, CultureInfo.InvariantCulture)}"
                ),
            ]);
        }

        rows.Add([
            InlineKeyboardButton.WithCallbackData(
                BotMessages.Wizard.DoneButton,
                $"{DonePrefix}:{requestId}"
            ),
        ]);

        return new InlineKeyboardMarkup(rows);
    }

    private static InlineKeyboardMarkup BuildHourKeyboard(
        int requestId,
        DateOnly date,
        HashSet<int> selected
    )
    {
        string dateToken = date.ToString(DateFormat, CultureInfo.InvariantCulture);
        List<InlineKeyboardButton> hourButtons = [];

        for (int hour = FirstHour; hour <= LastHour; hour++)
        {
            string label = selected.Contains(hour) ? $"✅ {hour:00}:00" : $"{hour:00}:00";
            hourButtons.Add(
                InlineKeyboardButton.WithCallbackData(
                    label,
                    $"{HourPrefix}:{requestId}:{dateToken}:{hour}"
                )
            );
        }

        // 3 hour buttons per row.
        List<InlineKeyboardButton[]> rows = hourButtons.Chunk(3).ToList();

        rows.Add([
            InlineKeyboardButton.WithCallbackData(
                BotMessages.Wizard.BackToDaysButton,
                $"{BackPrefix}:{requestId}"
            ),
            InlineKeyboardButton.WithCallbackData(
                BotMessages.Wizard.DoneButton,
                $"{DonePrefix}:{requestId}"
            ),
        ]);

        return new InlineKeyboardMarkup(rows);
    }

    private static DateOnly ParseDate(string token) =>
        DateOnly.ParseExact(token, DateFormat, CultureInfo.InvariantCulture);

    private static string FormatDay(DateOnly date)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        string label = date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        if (date == today)
        {
            return BotMessages.Wizard.Today(label);
        }

        if (date == today.AddDays(1))
        {
            return BotMessages.Wizard.Tomorrow(label);
        }

        return label;
    }

    private Task Answer(CallbackQuery query, string? text = null) =>
        bot.AnswerCallbackQuery(query.Id, text);
}
