using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public static class BackgroundServiceActivator
{
    public static void Start(IServiceProvider services, ILogger? logger = null)
    {
        logger?.LogInformation("Initializing background services");

        services.GetRequiredService<IPlanWatcherService>();
        logger?.LogInformation("PlanWatcherService initialized");

        services.GetRequiredService<IInboxWatcherService>();
        logger?.LogInformation("InboxWatcherService initialized");

        foreach (var startable in services.GetServices<IStartable>())
            startable.Start();
        logger?.LogInformation("Startable services initialized");

        var syncService = services.GetRequiredService<PlanDatabaseSyncService>();
        _ = Task.Run(syncService.PerformInitialSync);
        logger?.LogInformation("PlanDatabaseSyncService initial sync started in background");
    }
}
