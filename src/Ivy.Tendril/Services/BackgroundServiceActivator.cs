using System.Diagnostics;
using Ivy.Helpers;
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
                CrashLog.Write($"[{DateTime.UtcNow:O}] BackgroundServiceActivator.StartAsync failed: {ex}");
                throw; // Re-throw so caller (CompleteStepView) can handle it
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
        {
            try
            {
                startable.Start();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to start {Service}", startable.GetType().Name);
                CrashLog.Write($"[{DateTime.UtcNow:O}] IStartable.Start failed for {startable.GetType().Name}: {ex}");
            }
        }
        logger?.LogInformation("Startable services initialized ({ElapsedMs}ms)", sw.ElapsedMilliseconds);

        sw.Restart();
        logger?.LogInformation("Resolving PlanDatabaseSyncService...");
        var syncService = services.GetRequiredService<PlanDatabaseSyncService>();
        logger?.LogInformation("PlanDatabaseSyncService initialized ({ElapsedMs}ms)", sw.ElapsedMilliseconds);

        _ = Task.Run(() =>
        {
            try
            {
                syncService.PerformInitialSync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Initial database sync threw unhandled exception");
                CrashLog.Write($"[{DateTime.UtcNow:O}] PerformInitialSync unhandled exception: {ex}");
            }
        });
        logger?.LogInformation("PlanDatabaseSyncService initial sync started in background");
    }
}
