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

        using IServiceScope scope = scopeFactory.CreateScope();
        BeerBotDbContext db = scope.ServiceProvider.GetRequiredService<BeerBotDbContext>();
        SuggestCommand suggest = scope.ServiceProvider.GetRequiredService<SuggestCommand>();

        List<MeetingRequest> openRequests = await db
            .MeetingRequests.Where(r => r.Status == MeetingRequestStatus.Open)
            .ToListAsync();

        foreach (MeetingRequest request in openRequests)
        {
            try
            {
                bool deadlinePassed = DateTime.UtcNow >= request.DeadlineAt;
                await suggest.PostIfReadyAsync(request, deadlinePassed);
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
}
