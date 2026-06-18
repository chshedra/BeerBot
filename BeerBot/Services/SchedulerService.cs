using BeerBot.Commands;
using BeerBot.Data;
using BeerBot.Models;
using Microsoft.EntityFrameworkCore;

namespace BeerBot.Services;

/// <summary>
/// Background service that periodically checks open meeting requests and posts a suggestion
/// once each one's deadline has passed (or everyone has replied).
/// </summary>
public class SchedulerService(IServiceScopeFactory scopeFactory, ILogger<SchedulerService> logger)
    : BackgroundService
{
    /// <summary>
    /// Runs a check every 15 minutes until cancellation.
    /// </summary>
    /// <param name="stoppingToken">Signals when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SchedulerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            await TickAsync();
        }
    }

    /// <summary>
    /// Loads every open meeting request and, in its own DI scope, asks <see cref="SuggestCommand"/>
    /// to post and close any whose deadline has passed. Per-request errors are logged and skipped.
    /// </summary>
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
