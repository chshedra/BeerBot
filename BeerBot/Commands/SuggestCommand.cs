using BeerBot.Data;
using BeerBot.Models;
using BeerBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BeerBot.Commands;

public class SuggestCommand(
    BeerBotDbContext db,
    ITelegramBotClient bot,
    OpenRouterClient gemini,
    OverlapFinder overlapFinder,
    ILogger<SuggestCommand> logger)
{
    public async Task ExecuteAsync(Message message)
    {
        var groupChatId = message.Chat.Id;

        var request = await db.MeetingRequests
            .FirstOrDefaultAsync(r => r.GroupChatId == groupChatId && r.Status == MeetingRequestStatus.Open);

        if (request is null)
        {
            await bot.SendMessage(groupChatId, "No active meeting request. Start one with /beertime!");
            return;
        }

        logger.LogInformation("Forced suggestion for MeetingRequest {Id}", request.Id);
        await PostSuggestionAsync(request, groupChatId);
    }

    internal async Task PostSuggestionAsync(MeetingRequest request, long groupChatId)
    {
        var availabilities = await db.Availabilities
            .Include(a => a.User)
            .Where(a => a.RequestId == request.Id)
            .ToListAsync();

        if (availabilities.Count == 0)
        {
            await bot.SendMessage(groupChatId, "No one has replied yet, so I can't suggest a time. 😔");
            return;
        }

        var memberAvailability = availabilities
            .Select(a => new UserAvailability(a.User.Name, a.ParsedSlotsJson))
            .ToList();

        var bestSlots = overlapFinder.FindBestSlots(memberAvailability);
        var suggestionText = await gemini.GenerateSuggestionAsync(memberAvailability);

        await bot.SendMessage(groupChatId, suggestionText);

        if (bestSlots.Count > 0)
        {
            var pollOptions = bestSlots
                .Select(s => new Telegram.Bot.Types.InputPollOption($"{Capitalize(s.Day)} {s.Start:HH:mm}–{s.End:HH:mm}"))
                .Concat([new Telegram.Bot.Types.InputPollOption("None of these work for me")])
                .ToArray();

            await bot.SendPoll(groupChatId, "🍺 When should we meet?", pollOptions, isAnonymous: false);
        }

        request.Status = MeetingRequestStatus.Closed;
        await db.SaveChangesAsync();
        logger.LogInformation("MeetingRequest {Id} closed after suggestion posted", request.Id);
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
