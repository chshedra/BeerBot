using BeerBot.Services;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BeerBot;

public class Worker(
    ITelegramBotClient bot,
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started — beginning long-poll loop");
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
