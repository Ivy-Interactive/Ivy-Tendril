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

        // Elect before anything else is resolved. The inbox watcher used to be resolved here, ahead
        // of the election, and its constructor swept the shared inbox as a side effect of the resolve
        // itself - so a duplicate instance resurrected every breadcrumb before it knew it was not the
        // master. Nothing may be resolved between this point and the IsMaster read below.
        sw.Restart();
        logger?.LogInformation("Running master election...");
        var election = services.GetRequiredService<IMasterElectionService>();
        election.Start();
        var isMaster = election.IsMaster && Environment.GetEnvironmentVariable("TENDRIL_NOT_MASTER") != "1";
        logger?.LogInformation("Master election complete: isMaster={IsMaster} ({ElapsedMs}ms)", isMaster, sw.ElapsedMilliseconds);

        sw.Restart();
        logger?.LogInformation("Starting IStartable services...");
        foreach (var startable in services.GetServices<IStartable>())
        {
            // The election instance is in this collection too, and it has already been started.
            if (ReferenceEquals(startable, election))
                continue;

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
        var masterOnly = new List<IMasterOnlyStartable>();
        if (isMaster)
        {
            foreach (var startable in services.GetServices<IMasterOnlyStartable>())
            {
                try
                {
                    logger?.LogInformation("Starting master-only service {Service}...", startable.GetType().Name);
                    startable.Start();
                    masterOnly.Add(startable);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to start {Service}", startable.GetType().Name);
                    CrashLog.Write($"[{DateTime.UtcNow:O}] IMasterOnlyStartable.Start failed for {startable.GetType().Name}: {ex}");
                }
            }
            logger?.LogInformation("Master-only services initialized ({ElapsedMs}ms)", sw.ElapsedMilliseconds);
        }
        else
        {
            // Deliberately never resolved: constructing one of these is itself a side effect on shared
            // state, which is the whole defect this gate exists to close. That is also why the names
            // are not listed here - enumerating them means resolving them.
            logger?.LogInformation("Skipping master-only background services (this instance is not master)");
            CrashLog.Write($"[{DateTime.UtcNow:O}] Not master: master-only background services were not resolved");
        }

        // A demotion is the one realistic way a running instance stops being the master (another
        // launch judged its claim stale). Stop only what was actually started, so demotion never
        // resolves a service this instance never constructed.
        election.MasterStatusChanged += nowMaster =>
        {
            if (nowMaster)
            {
                foreach (var startable in masterOnly)
                {
                    try
                    {
                        startable.Start();
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Failed to restart {Service} after promotion", startable.GetType().Name);
                    }
                }

                logger?.LogInformation("Promoted to master: restarted {Count} master-only services", masterOnly.Count);
                return;
            }

            foreach (var startable in masterOnly)
            {
                try
                {
                    startable.Stop();
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to stop {Service} after demotion", startable.GetType().Name);
                }
            }

            logger?.LogWarning("Demoted from master: stopped {Count} master-only services", masterOnly.Count);
            CrashLog.Write($"[{DateTime.UtcNow:O}] Demoted from master, stopped {masterOnly.Count} master-only services");
        };

        sw.Restart();
        logger?.LogInformation("Resolving PlanDatabaseSyncService...");
        var syncService = services.GetRequiredService<PlanDatabaseSyncService>();
        logger?.LogInformation("PlanDatabaseSyncService initialized ({ElapsedMs}ms)", sw.ElapsedMilliseconds);

        // Resolved either way (the UI queries through it), but only the master writes: two instances
        // upserting identical rows is idempotent and pointless lock contention on one SQLite file.
        // The watcher-driven writes are gated inside the service itself; the initial sync here.
        if (!isMaster)
        {
            logger?.LogInformation("Skipping initial database sync (this instance is not master)");
            return;
        }

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
