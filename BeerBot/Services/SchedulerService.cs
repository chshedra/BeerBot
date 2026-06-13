using BeerBot.Commands;
using BeerBot.Data;
using BeerBot.Models;
using Microsoft.EntityFrameworkCore;

namespace BeerBot.Services;

public class SchedulerService(IServiceScopeFactory scopeFactory, ILogger<SchedulerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SchedulerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            await TickAsync();
        }
    }

    private async Task TickAsync()
    {
        logger.LogInformation("SchedulerService: checking open meeting requests");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeerBotDbContext>();
        var suggest = scope.ServiceProvider.GetRequiredService<SuggestCommand>();

        var openRequests = await db
            .MeetingRequests.Where(r => r.Status == MeetingRequestStatus.Open)
            .ToListAsync();

        foreach (var request in openRequests)
        {
            try
            {
                await TryCloseMeetingRequestAsync(db, suggest, request);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "SchedulerService: error processing MeetingRequest {Id}",
                    request.Id
                );
            }
        }
    }

    private async Task TryCloseMeetingRequestAsync(
        BeerBotDbContext db,
        SuggestCommand suggest,
        MeetingRequest request
    )
    {
        var users = await db.Users.Where(u => u.GroupChatId == request.GroupChatId).ToListAsync();

        var repliedUserIds = await db
            .Availabilities.Where(a => a.RequestId == request.Id)
            .Select(a => a.UserId)
            .ToListAsync();

        var allReplied = users.Count > 0 && users.All(u => repliedUserIds.Contains(u.Id));
        var deadlinePassed = DateTime.UtcNow >= request.DeadlineAt;

        if (!allReplied && !deadlinePassed)
            return;

        var reason = allReplied ? "all members replied" : "deadline passed";
        logger.LogInformation("MeetingRequest {Id}: closing ({Reason})", request.Id, reason);

        await suggest.PostSuggestionAsync(request, request.GroupChatId);
    }
}
