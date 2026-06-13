using BeerBot.Data;
using BeerBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using User = BeerBot.Models.User;

namespace BeerBot.Commands;

public class StatusCommand(
    BeerBotDbContext db,
    ITelegramBotClient bot,
    ILogger<StatusCommand> logger
)
{
    public async Task ExecuteAsync(Message message)
    {
        long groupChatId = message.Chat.Id;

        MeetingRequest? request = await db.MeetingRequests.FirstOrDefaultAsync(r =>
            r.GroupChatId == groupChatId && r.Status == MeetingRequestStatus.Open
        );

        if (request is null)
        {
            await bot.SendMessage(
                groupChatId,
                "No active meeting request. Start one with /beertime!"
            );
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
        sb.AppendLine(
            $"📊 *Meeting request status* (deadline: {request.DeadlineAt:ddd HH:mm} UTC)"
        );
        sb.AppendLine();

        foreach (User u in users)
        {
            sb.AppendLine(repliedSet.Contains(u.Id) ? $"✅ {u.Name}" : $"⏳ {u.Name}");
        }

        if (waiting.Count == 0)
        {
            sb.AppendLine("\nEveryone has replied! Generating suggestion...");
        }
        else
        {
            sb.AppendLine($"\nWaiting for {waiting.Count} more.");
        }

        logger.LogInformation("Status requested for group {GroupChatId}", groupChatId);
        await bot.SendMessage(
            groupChatId,
            sb.ToString(),
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
        );
    }
}
