using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public static class BackgroundServiceActivator
{
    public static async Task StartAsync(IServiceProvider services, ILogger? logger = null)
    {
        logger?.LogInformation("Initializing background services asynchronously");

        await Task.Run(() =>
        {
            try
            {
                Start(services, logger);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to initialize background services");
                throw;
            }
        });
    }

    public static void Start(IServiceProvider services, ILogger? logger = null)
    {
        logger?.LogInformation("Initializing background services");

        var sw = Stopwatch.StartNew();

        logger?.LogInformation("Resolving PlanWatcherService...");
        services.GetRequiredService<IPlanWatcherService>();
        logger?.LogInformation("PlanWatcherService initialized ({ElapsedMs}ms)", sw.ElapsedMilliseconds);

        sw.Restart();
        logger?.LogInformation("Resolving InboxWatcherService...");
        services.GetRequiredService<IInboxWatcherService>();
        logger?.LogInformation("InboxWatcherService initialized ({ElapsedMs}ms)", sw.ElapsedMilliseconds);

        sw.Restart();
        logger?.LogInformation("Starting IStartable services...");
        foreach (var startable in services.GetServices<IStartable>())
            startable.Start();
        logger?.LogInformation("Startable services initialized ({ElapsedMs}ms)", sw.ElapsedMilliseconds);

        sw.Restart();
        logger?.LogInformation("Resolving PlanDatabaseSyncService...");
        var syncService = services.GetRequiredService<PlanDatabaseSyncService>();
        logger?.LogInformation("PlanDatabaseSyncService initialized ({ElapsedMs}ms)", sw.ElapsedMilliseconds);

        _ = Task.Run(syncService.PerformInitialSync);
        logger?.LogInformation("PlanDatabaseSyncService initial sync started in background");
    }
}
