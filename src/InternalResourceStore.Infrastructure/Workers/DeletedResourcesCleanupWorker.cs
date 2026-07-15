using InternalResourceStore.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InternalResourceStore.Infrastructure.Workers;

public sealed class DeletedResourcesCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<DeletedResourcesCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delayMinutes = 60;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<CleanupDeletedResourcesService>();
                var result = await service.CleanupAsync(100, stoppingToken);
                delayMinutes = await service.GetCleanupIntervalMinutesAsync(stoppingToken);

                if (result.PurgedCount > 0)
                    logger.LogInformation("Purged {PurgedCount} deleted resources.", result.PurgedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Deleted resources cleanup failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, delayMinutes)), stoppingToken);
        }
    }
}
