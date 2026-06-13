using BeerBot.Data;
using BeerBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BeerBot.Commands;

public class StatusCommand(
    BeerBotDbContext db,
    ITelegramBotClient bot,
    ILogger<StatusCommand> logger
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
            await bot.SendMessage(
                groupChatId,
                "No active meeting request. Start one with /beertime!"
            );
            return;
        }

        var users = await db.Users.Where(u => u.GroupChatId == groupChatId).ToListAsync();
        var replied = await db
            .Availabilities.Where(a => a.RequestId == request.Id && a.Submitted)
            .Select(a => a.UserId)
            .ToListAsync();

        var repliedSet = replied.ToHashSet();
        var waiting = users.Where(u => !repliedSet.Contains(u.Id)).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            $"📊 *Meeting request status* (deadline: {request.DeadlineAt:ddd HH:mm} UTC)"
        );
        sb.AppendLine();

        foreach (var u in users)
            sb.AppendLine(repliedSet.Contains(u.Id) ? $"✅ {u.Name}" : $"⏳ {u.Name}");

        if (waiting.Count == 0)
            sb.AppendLine("\nEveryone has replied! Generating suggestion...");
        else
            sb.AppendLine($"\nWaiting for {waiting.Count} more.");

        logger.LogInformation("Status requested for group {GroupChatId}", groupChatId);
        await bot.SendMessage(
            groupChatId,
            sb.ToString(),
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
        );
    }
}
