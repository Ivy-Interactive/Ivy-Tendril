using System.Reflection;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class BackgroundServiceActivatorTests : IAsyncLifetime
{
    private readonly string _tempDir;
    private ServiceProvider? _serviceProvider;

    public BackgroundServiceActivatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-activator-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "Plans"));
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();

            // Give services time to complete disposal - use polling instead of fixed delay
            await RetryHelper.WaitUntilAsync(
                async () =>
                {
                    await Task.Yield();
                    // Check if temp directory can be safely deleted (no file locks)
                    try
                    {
                        if (Directory.Exists(_tempDir))
                        {
                            // Try to enumerate - will fail if services still have locks
                            _ = Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories);
                        }
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                },
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(50),
                "Services did not fully dispose within timeout");
        }

        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private ServiceProvider BuildServiceProvider(bool isMaster = true, MockMasterElection? election = null)
    {
        var settings = new TendrilSettings();
        var config = new ConfigService(settings, _tempDir);

        var services = new ServiceCollection();
        services.AddSingleton<IConfigService>(config);
        services.AddSingleton<ConfigService>(config);
        services.AddSingleton<IMasterElectionService>(election ?? new MockMasterElection(isMaster));
        services.AddSingleton<IPlanWatcherService>(new PlanWatcherService(config));
        services.AddSingleton<IInboxWatcherService>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfigService>();
            var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
            return new InboxWatcherService(cfg, jobService, NullLogger<InboxWatcherService>.Instance);
        });
        services.AddSingleton<WorktreeCleanupService>(sp =>
            new WorktreeCleanupService(Path.Combine(_tempDir, "Plans"), NullLogger<WorktreeCleanupService>.Instance));
        services.AddSingleton<IStartable>(sp => sp.GetRequiredService<WorktreeCleanupService>());

        // A factory rather than an instance, so "was it ever constructed" is a meaningful assertion.
        services.AddSingleton<IMasterOnlyStartable>(sp => new MockMasterOnlyStartable());
        services.AddSingleton<IPlanDatabaseService>(sp =>
        {
            var dbPath = Path.Combine(_tempDir, "tendril.db");
            return new PlanDatabaseService(dbPath, NullLogger<PlanDatabaseService>.Instance);
        });
        services.AddSingleton<PlanDatabaseSyncService>(sp =>
        {
            var planReader = new PlanReaderService(config, NullLogger<PlanReaderService>.Instance);
            var database = sp.GetRequiredService<IPlanDatabaseService>();
            var watcher = sp.GetRequiredService<IPlanWatcherService>();
            return new PlanDatabaseSyncService(planReader, database, watcher, config,
                NullLogger<PlanDatabaseSyncService>.Instance);
        });

        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }

    [Fact]
    public void Start_ResolvesAllExpectedServices()
    {
        var sp = BuildServiceProvider();

        // Should not throw — all services are registered and resolvable
        BackgroundServiceActivator.Start(sp);

        // Verify the services were resolved by checking they exist in the container
        var planWatcher = sp.GetRequiredService<IPlanWatcherService>();
        var inboxWatcher = sp.GetRequiredService<IInboxWatcherService>();
        var worktreeCleanup = sp.GetRequiredService<WorktreeCleanupService>();
        var syncService = sp.GetRequiredService<PlanDatabaseSyncService>();

        Assert.NotNull(planWatcher);
        Assert.NotNull(inboxWatcher);
        Assert.NotNull(worktreeCleanup);
        Assert.NotNull(syncService);
    }

    [Fact]
    public void Start_ThrowsWhenServiceMissing()
    {
        // Register only some services — omit IPlanWatcherService
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => BackgroundServiceActivator.Start(sp));
    }

    [Fact]
    public void JobService_Resolves_WhenTendrilHomeEmpty_WithoutTouchingPlanDatabaseService()
    {
        // Simulates fresh-install DI resolution: TendrilHome is empty,
        // so IPlanDatabaseService registration would throw if resolved.
        // The JobService factory in TendrilServer must avoid that resolution
        // when TendrilHome is empty, and pass null for the database dependency.

        var settings = new TendrilSettings();
        var config = new FreshInstallConfigService(settings);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfigService>(config);
        services.AddSingleton<IAgentRunner>(new AgentRunner());
        services.AddSingleton<IModelPricingProvider>(new ModelPricingProvider());
        services.AddSingleton<ModelPricingService>();
        services.AddSingleton<IPlanReaderService>(sp =>
            new PlanReaderService(sp.GetRequiredService<IConfigService>(), NullLogger<PlanReaderService>.Instance));
        services.AddSingleton<ITelemetryService>(sp => new TelemetryService(false));
        services.AddSingleton<IPlanWatcherService>(new PlanWatcherService(config));

        // Mirrors TendrilServer line 63-71: factory throws when TendrilHome is empty.
        services.AddSingleton<IPlanDatabaseService>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfigService>();
            if (string.IsNullOrEmpty(cfg.TendrilHome))
                throw new InvalidOperationException(
                    "Cannot create PlanDatabaseService: TendrilHome is not configured. Complete onboarding first.");
            throw new InvalidOperationException("Test should not reach database construction.");
        });

        // Mirrors TendrilServer line 89-99: fixed factory that conditionally resolves the database.
        services.AddSingleton<JobService>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfigService>();
            return new JobService(
                cfg,
                null,
                sp.GetRequiredService<ModelPricingService>(),
                sp.GetRequiredService<IPlanReaderService>(),
                sp.GetRequiredService<ITelemetryService>(),
                sp.GetRequiredService<IPlanWatcherService>(),
                string.IsNullOrEmpty(cfg.TendrilHome) ? null : sp.GetRequiredService<IPlanDatabaseService>());
        });

        _serviceProvider = services.BuildServiceProvider();

        // Resolution must NOT throw — this is the crash we're fixing.
        var jobService = _serviceProvider.GetRequiredService<JobService>();
        Assert.NotNull(jobService);

        // Verify the database field is null so JobService won't crash at runtime.
        var databaseField = typeof(JobService).GetField("_database",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(databaseField);
        Assert.Null(databaseField!.GetValue(jobService));
    }

    [Fact]
    public void Start_CallsStartOnAllRegisteredIStartables()
    {
        var startable1 = new MockStartable();
        var startable2 = new MockStartable();

        var settings = new TendrilSettings();
        var config = new ConfigService(settings, _tempDir);

        var services = new ServiceCollection();
        services.AddSingleton<IConfigService>(config);
        services.AddSingleton<ConfigService>(config);
        services.AddSingleton<IMasterElectionService>(new MockMasterElection());
        services.AddSingleton<IPlanWatcherService>(new PlanWatcherService(config));
        services.AddSingleton<IInboxWatcherService>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfigService>();
            var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
            return new InboxWatcherService(cfg, jobService, NullLogger<InboxWatcherService>.Instance);
        });
        services.AddSingleton<WorktreeCleanupService>(sp =>
            new WorktreeCleanupService(Path.Combine(_tempDir, "Plans"), NullLogger<WorktreeCleanupService>.Instance));
        services.AddSingleton<IStartable>(startable1);
        services.AddSingleton<IStartable>(startable2);
        services.AddSingleton<IPlanDatabaseService>(sp =>
        {
            var dbPath = Path.Combine(_tempDir, "tendril.db");
            return new PlanDatabaseService(dbPath, NullLogger<PlanDatabaseService>.Instance);
        });
        services.AddSingleton<PlanDatabaseSyncService>(sp =>
        {
            var planReader = new PlanReaderService(config, NullLogger<PlanReaderService>.Instance);
            var database = sp.GetRequiredService<IPlanDatabaseService>();
            var watcher = sp.GetRequiredService<IPlanWatcherService>();
            return new PlanDatabaseSyncService(planReader, database, watcher, config,
                NullLogger<PlanDatabaseSyncService>.Instance);
        });

        _serviceProvider = services.BuildServiceProvider();

        BackgroundServiceActivator.Start(_serviceProvider);

        Assert.True(startable1.Started);
        Assert.True(startable2.Started);
    }

    private sealed class FreshInstallConfigService : IConfigService
    {
        public FreshInstallConfigService(TendrilSettings settings)
        {
            Settings = settings;
        }

        public TendrilSettings Settings { get; }
        public string TendrilHome => "";
        public string ConfigPath => "";
        public string PlanFolder => "";
        public List<ProjectConfig> Projects => Settings.Projects;
        public List<LevelConfig> Levels => Settings.Levels;
        public string[] LevelNames => Array.Empty<string>();
        public EditorConfig Editor => Settings.Editor ?? new EditorConfig();
        public bool NeedsOnboarding => true;
        public ConfigParseError? ParseError => null;

        public ProjectConfig? GetProject(string name)
        {
            return null;
        }

        public Colors? GetLevelColor(string level)
        {
            return null;
        }

        public Colors? GetProjectColor(string projectName)
        {
            return null;
        }

        public void SaveSettings()
        {
        }

        public void MutateAndSave(Action<TendrilSettings> mutate)
        {
            mutate(Settings);
        }

        public void ReloadSettings()
        {
        }

        public bool TryAutoHeal()
        {
            return false;
        }

        public void ResetToDefaults()
        {
        }

        public void RetryLoadConfig()
        {
        }
#pragma warning disable CS0067
        public event EventHandler? SettingsReloaded;
#pragma warning restore CS0067
        public void SetPendingCodingAgent(string name)
        {
        }

        public string? GetPendingCodingAgent()
        {
            return null;
        }

        public void SetPendingTendrilHome(string path)
        {
        }

        public string? GetPendingTendrilHome()
        {
            return null;
        }

        public void SetPendingProject(ProjectConfig project)
        {
        }

        public ProjectConfig? GetPendingProject()
        {
            return null;
        }

        public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions)
        {
        }

        public List<VerificationConfig>? GetPendingVerificationDefinitions()
        {
            return null;
        }

        public void CompleteOnboarding(string tendrilHome)
        {
        }

        public void OpenInEditor(string path)
        {
        }

        public string PolishMarkdown(string content)
        {
            return content;
        }

        public void Dispose()
        {
        }
    }

    private class MockStartable : IStartable
    {
        public bool Started { get; private set; }

        public void Start()
        {
            Started = true;
        }
    }

    /// <summary>
    ///     Stands in for the real election so a test can decide the verdict without a .master file, and
    ///     can force a demotion afterwards.
    /// </summary>
    private sealed class MockMasterElection(bool isMaster = true) : IMasterElectionService
    {
        public bool Started { get; private set; }
        public bool IsMaster { get; private set; } = isMaster;

        public event Action<bool>? MasterStatusChanged;

        public void Start()
        {
            Started = true;
        }

        public void Demote()
        {
            IsMaster = false;
            MasterStatusChanged?.Invoke(false);
        }

        public void Promote()
        {
            IsMaster = true;
            MasterStatusChanged?.Invoke(true);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    ///     Records construction as well as Start/Stop: the point of the gate is that a non-master never
    ///     even constructs one of these, because construction is itself a side effect on shared state.
    /// </summary>
    private sealed class MockMasterOnlyStartable : IMasterOnlyStartable
    {
        public static int Constructions;

        public MockMasterOnlyStartable()
        {
            Interlocked.Increment(ref Constructions);
        }

        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public void Start()
        {
            StartCount++;
        }

        public void Stop()
        {
            StopCount++;
        }
    }

    [Fact]
    public void Start_StartsMasterOnlyServices_WhenMaster()
    {
        MockMasterOnlyStartable.Constructions = 0;
        var sp = BuildServiceProvider();

        BackgroundServiceActivator.Start(sp);

        var masterOnly = Assert.IsType<MockMasterOnlyStartable>(sp.GetRequiredService<IMasterOnlyStartable>());
        Assert.Equal(1, masterOnly.StartCount);
        Assert.Equal(0, masterOnly.StopCount);
    }

    [Fact]
    public void Start_DoesNotConstructMasterOnlyServices_WhenNotMaster()
    {
        MockMasterOnlyStartable.Constructions = 0;
        var sp = BuildServiceProvider(isMaster: false);

        BackgroundServiceActivator.Start(sp);

        // Not "was not started" but "was not built": the defect was a constructor that swept the
        // shared inbox as a side effect of the DI resolve.
        Assert.Equal(0, MockMasterOnlyStartable.Constructions);
    }

    [Fact]
    public void Start_StopsMasterOnlyServices_OnDemotion()
    {
        MockMasterOnlyStartable.Constructions = 0;
        var election = new MockMasterElection();
        var sp = BuildServiceProvider(election: election);

        BackgroundServiceActivator.Start(sp);
        var masterOnly = (MockMasterOnlyStartable)sp.GetRequiredService<IMasterOnlyStartable>();
        Assert.Equal(1, masterOnly.StartCount);

        election.Demote();
        Assert.Equal(1, masterOnly.StopCount);

        election.Promote();
        Assert.Equal(2, masterOnly.StartCount);
    }

    [Fact]
    public void Start_ElectsBeforeStartingAnything()
    {
        var election = new MockMasterElection();
        var sp = BuildServiceProvider(election: election);

        BackgroundServiceActivator.Start(sp);

        Assert.True(election.Started);
    }
}
