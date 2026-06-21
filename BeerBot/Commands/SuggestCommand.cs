using System.Globalization;
using System.Text;
using BeerBot.Data;
using BeerBot.Models;
using BeerBot.Resources;
using BeerBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using User = BeerBot.Models.User;

namespace BeerBot.Commands;

/// <summary>
/// Builds and posts the meeting suggestion (best shared time windows plus a poll) to the group.
/// Triggered manually via /suggest, on the final submission, or by the deadline scheduler.
/// </summary>
public class SuggestCommand(
    BeerBotDbContext db,
    ITelegramBotClient bot,
    OverlapFinder overlapFinder,
    ILogger<SuggestCommand> logger
)
{
    /// <summary>
    /// Handles /suggest in a group: immediately posts a suggestion for the open round,
    /// regardless of how many members have replied. No-ops with a notice if no round is open.
    /// </summary>
    /// <param name="message">The /suggest message received in the group chat.</param>
    public async Task ExecuteAsync(Message message)
    {
        long groupChatId = message.Chat.Id;

        MeetingRequest? request = await db.MeetingRequests.FirstOrDefaultAsync(r =>
            r.GroupChatId == groupChatId && r.Status == MeetingRequestStatus.Open
        );

        if (request is null)
        {
            await bot.SendMessage(groupChatId, BotMessages.Suggest.NoActiveRound);
            return;
        }

        logger.LogInformation("Forced suggestion for MeetingRequest {Id}", request.Id);
        await PostSuggestionAsync(request, groupChatId);
    }

    /// <summary>
    /// Posts the suggestion and closes the round if every member has submitted or the deadline
    /// has passed; otherwise does nothing.
    /// </summary>
    /// <param name="request">The open meeting request to evaluate.</param>
    /// <param name="deadlinePassed">True if the request's deadline has elapsed.</param>
    /// <returns>True if the request was closed; false if it is still waiting for replies.</returns>
    internal async Task<bool> PostIfReadyAsync(MeetingRequest request, bool deadlinePassed)
    {
        List<User> users = await db
            .Users.Where(u => u.GroupChatId == request.GroupChatId)
            .ToListAsync();

        List<int> repliedUserIds = await db
            .Availabilities.Where(a => a.RequestId == request.Id && a.Submitted)
            .Select(a => a.UserId)
            .ToListAsync();

        bool allReplied = users.Count > 0 && users.All(u => repliedUserIds.Contains(u.Id));

        if (!allReplied && !deadlinePassed)
        {
            return false;
        }

        string reason = allReplied ? "all members replied" : "deadline passed";
        logger.LogInformation("MeetingRequest {Id}: closing ({Reason})", request.Id, reason);

        await PostSuggestionAsync(request, request.GroupChatId);
        return true;
    }

    /// <summary>
    /// Gathers all submitted availabilities, computes the best overlapping windows, and posts a
    /// summary message plus a native poll to the group, then marks the request closed. Posts a
    /// fallback notice if nobody picked or no window overlapped.
    /// </summary>
    /// <param name="request">The request to summarize and close.</param>
    /// <param name="groupChatId">The group chat to post the suggestion to.</param>
    internal async Task PostSuggestionAsync(MeetingRequest request, long groupChatId)
    {
        // Atomically claim the round by flipping Open -> Closed in a single statement. The submit
        // handler, the deadline scheduler, and /suggest can all reach here concurrently (each in its
        // own DbContext scope); the conditional update lets exactly one of them win, so the group
        // never gets a duplicate suggestion + poll. A zero-row result means someone else already
        // posted.
        int claimed = await db
            .MeetingRequests.Where(r => r.Id == request.Id && r.Status == MeetingRequestStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, MeetingRequestStatus.Closed));

        if (claimed == 0)
        {
            logger.LogInformation(
                "MeetingRequest {Id}: already closed by another trigger, skipping suggestion",
                request.Id
            );
            return;
        }

        List<Availability> availabilities = await db
            .Availabilities.Include(a => a.User)
            .Include(a => a.Slots)
            .Where(a => a.RequestId == request.Id && a.Submitted)
            .ToListAsync();

        if (availabilities.Count == 0)
        {
            await bot.SendMessage(groupChatId, BotMessages.Suggest.NobodyPicked);
            return;
        }

        List<UserAvailability> memberAvailability = availabilities
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

        List<SuggestedSlot> bestSlots = overlapFinder.FindBestSlots(memberAvailability);

        if (bestSlots.Count == 0)
        {
            await bot.SendMessage(groupChatId, BotMessages.Suggest.NoOverlap);
        }
        else
        {
            StringBuilder sb = new();
            sb.AppendLine(BotMessages.Suggest.BestTimeHeader);
            sb.AppendLine();
            foreach (SuggestedSlot slot in bestSlots)
            {
                sb.AppendLine(
                    BotMessages.Suggest.SlotLine(
                        FormatSlot(slot),
                        slot.MemberCount,
                        availabilities.Count
                    )
                );
            }

            await bot.SendMessage(groupChatId, sb.ToString());

            InputPollOption[] pollOptions = bestSlots
                .Select(s => new InputPollOption(FormatSlot(s)))
                .Concat([new InputPollOption(BotMessages.Suggest.PollNoneOption)])
                .ToArray();

            await bot.SendPoll(
                groupChatId,
                BotMessages.Suggest.PollQuestion,
                pollOptions,
                isAnonymous: false
            );
        }

        logger.LogInformation("MeetingRequest {Id} closed after suggestion posted", request.Id);
    }

    /// <summary>Formats a suggested slot as a short human-readable "Day HH:mm–HH:mm" string.</summary>
    private static string FormatSlot(SuggestedSlot s) =>
        $"{s.Day.ToString("ddd d MMM", CultureInfo.InvariantCulture)} {s.Start:HH:mm}–{s.End:HH:mm}";
}
