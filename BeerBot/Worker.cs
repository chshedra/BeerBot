using BeerBot.Services;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BeerBot;

/// <summary>
/// Background service that runs the Telegram long-polling loop, fetching updates and
/// dispatching each to a <see cref="BotUpdateHandler"/>. Errors are logged so the loop
/// keeps polling.
/// </summary>
public class Worker(
    ITelegramBotClient bot,
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger
) : BackgroundService
{
    /// <summary>
    /// Long-polls Telegram for updates until cancellation, dispatching each update for
    /// processing and advancing the offset. Backs off briefly after a polling error.
    /// </summary>
    /// <param name="stoppingToken">Signals when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started, beginning long-poll loop");
        int offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Update[] updates = await bot.GetUpdates(
                    offset: offset,
                    timeout: 30,
                    cancellationToken: stoppingToken
                );

                foreach (Update update in updates)
                {
                    offset = update.Id + 1;
                    _ = ProcessUpdateAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker: error during GetUpdates");
                await Task.Delay(5_000, stoppingToken);
            }
        }

        logger.LogInformation("Worker stopped");
    }

    /// <summary>
    /// Processes a single update in its own DI scope so each gets a fresh scoped handler and
    /// <c>DbContext</c>. Unhandled errors are logged and swallowed to protect the poll loop.
    /// </summary>
    /// <param name="update">The Telegram update to handle.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    private async Task ProcessUpdateAsync(Update update, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotUpdateHandler handler = scope.ServiceProvider.GetRequiredService<BotUpdateHandler>();
        try
        {
            await handler.HandleAsync(update);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Worker: unhandled error processing update {UpdateId}", update.Id);
        }
    }
}
