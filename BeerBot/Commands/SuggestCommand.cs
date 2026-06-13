using System.Globalization;
using System.Text;
using BeerBot.Data;
using BeerBot.Models;
using BeerBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BeerBot.Commands;

public class SuggestCommand(
    BeerBotDbContext db,
    ITelegramBotClient bot,
    OverlapFinder overlapFinder,
    ILogger<SuggestCommand> logger
)
{
    public async Task ExecuteAsync(Message message)
    {
        var groupChatId = message.Chat.Id;

        var request = await db.MeetingRequests.FirstOrDefaultAsync(r =>
            r.GroupChatId == groupChatId && r.Status == MeetingRequestStatus.Open
        );

        if (request is null)
        {
            await bot.SendMessage(groupChatId, "Нет активного раунда. Начни в личке: /beertime");
            return;
        }

        logger.LogInformation("Forced suggestion for MeetingRequest {Id}", request.Id);
        await PostSuggestionAsync(request, groupChatId);
    }

    // Closes and posts the suggestion if everyone has submitted or the deadline passed.
    // Returns true if the request was closed.
    internal async Task<bool> PostIfReadyAsync(MeetingRequest request, bool deadlinePassed)
    {
        var users = await db.Users.Where(u => u.GroupChatId == request.GroupChatId).ToListAsync();

        var repliedUserIds = await db
            .Availabilities.Where(a => a.RequestId == request.Id && a.Submitted)
            .Select(a => a.UserId)
            .ToListAsync();

        var allReplied = users.Count > 0 && users.All(u => repliedUserIds.Contains(u.Id));

        if (!allReplied && !deadlinePassed)
            return false;

        var reason = allReplied ? "all members replied" : "deadline passed";
        logger.LogInformation("MeetingRequest {Id}: closing ({Reason})", request.Id, reason);

        await PostSuggestionAsync(request, request.GroupChatId);
        return true;
    }

    internal async Task PostSuggestionAsync(MeetingRequest request, long groupChatId)
    {
        var availabilities = await db
            .Availabilities.Include(a => a.User)
            .Include(a => a.Slots)
            .Where(a => a.RequestId == request.Id && a.Submitted)
            .ToListAsync();

        if (availabilities.Count == 0)
        {
            await bot.SendMessage(
                groupChatId,
                "Пока никто не выбрал время — нечего предложить. 😔"
            );
            request.Status = MeetingRequestStatus.Closed;
            await db.SaveChangesAsync();
            return;
        }

        var memberAvailability = availabilities
            .Select(a => new UserAvailability(
                a.User.Name,
                a.Slots.Select(s => new TimeSlot
                    {
                        Day = s.Date,
                        StartTime = s.Start,
                        EndTime = s.End,
                    })
                    .ToList()
            ))
            .ToList();

        var bestSlots = overlapFinder.FindBestSlots(memberAvailability);

        if (bestSlots.Count == 0)
        {
            await bot.SendMessage(
                groupChatId,
                "Не нашёл общего окна — ни один слот не пересёкся. 😔"
            );
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine("🍺 Лучшее время для встречи:");
            sb.AppendLine();
            foreach (var slot in bestSlots)
                sb.AppendLine(
                    $"• {FormatSlot(slot)} — свободны {slot.MemberCount}/{availabilities.Count}"
                );

            await bot.SendMessage(groupChatId, sb.ToString());

            var pollOptions = bestSlots
                .Select(s => new InputPollOption(FormatSlot(s)))
                .Concat([new InputPollOption("Ни один не подходит")])
                .ToArray();

            await bot.SendPoll(
                groupChatId,
                "🍺 Когда встречаемся?",
                pollOptions,
                isAnonymous: false
            );
        }

        request.Status = MeetingRequestStatus.Closed;
        await db.SaveChangesAsync();
        logger.LogInformation("MeetingRequest {Id} closed after suggestion posted", request.Id);
    }

    private static string FormatSlot(SuggestedSlot s) =>
        $"{s.Day.ToString("ddd d MMM", CultureInfo.InvariantCulture)} {s.Start:HH:mm}–{s.End:HH:mm}";
}
