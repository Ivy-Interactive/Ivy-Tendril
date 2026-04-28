using System.Reflection;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.SessionParsers;
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

    private ServiceProvider BuildServiceProvider()
    {
        var settings = new TendrilSettings();
        var config = new ConfigService(settings, _tempDir);

        var services = new ServiceCollection();
        services.AddSingleton<IConfigService>(config);
        services.AddSingleton<ConfigService>(config);
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
            return new PlanDatabaseSyncService(planReader, database, watcher,
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
        services.AddSingleton<ISessionParser, ClaudeSessionParser>();
        services.AddSingleton<ISessionParser, CodexSessionParser>();
        services.AddSingleton<ISessionParser, GeminiSessionParser>();
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
            return new PlanDatabaseSyncService(planReader, database, watcher,
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

        public BadgeVariant GetBadgeVariant(string level)
        {
            return BadgeVariant.Outline;
        }

        public Colors? GetProjectColor(string projectName)
        {
            return null;
        }

        public void SaveSettings()
        {
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

        public string PreprocessForEditing(string path)
        {
            return path;
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
}