using BeerBot.Data;
using BeerBot.Models;
using BeerBot.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using User = BeerBot.Models.User;

namespace BeerBot.Commands;

/// <summary>
/// Handles /status in a group chat: reports who has replied to the open round and who is
/// still pending.
/// </summary>
public class StatusCommand(
    BeerBotDbContext db,
    ITelegramBotClient bot,
    ILogger<StatusCommand> logger
)
{
    /// <summary>
    /// Posts a status message to the group listing each member as replied or waiting for the
    /// currently open round. No-ops with a notice if there is no open round.
    /// </summary>
    /// <param name="message">The /status message received in the group chat.</param>
    public async Task ExecuteAsync(Message message)
    {
        long groupChatId = message.Chat.Id;

        MeetingRequest? request = await db.MeetingRequests.FirstOrDefaultAsync(r =>
            r.GroupChatId == groupChatId && r.Status == MeetingRequestStatus.Open
        );

        if (request is null)
        {
            await bot.SendMessage(groupChatId, BotMessages.Status.NoActiveRequest);
            return;
        }

        List<User> users = await db.Users.Where(u => u.GroupChatId == groupChatId).ToListAsync();
        List<int> replied = await db
            .Availabilities.Where(a => a.RequestId == request.Id && a.Submitted)
            .Select(a => a.UserId)
            .ToListAsync();

        HashSet<int> repliedSet = replied.ToHashSet();
        List<User> waiting = users.Where(u => !repliedSet.Contains(u.Id)).ToList();

        System.Text.StringBuilder sb = new();
        sb.AppendLine(BotMessages.Status.Header(request.DeadlineAt));
        sb.AppendLine();

        foreach (User u in users)
        {
            sb.AppendLine(
                repliedSet.Contains(u.Id)
                    ? BotMessages.Status.MemberReplied(u.Name)
                    : BotMessages.Status.MemberWaiting(u.Name)
            );
        }

        if (waiting.Count == 0)
        {
            sb.AppendLine(BotMessages.Status.EveryoneReplied);
        }
        else
        {
            sb.AppendLine(BotMessages.Status.WaitingForMore(waiting.Count));
        }

        logger.LogInformation("Status requested for group {GroupChatId}", groupChatId);
        await bot.SendMessage(
            groupChatId,
            sb.ToString(),
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
        );
    }
}
