using System.ClientModel;
using Ivy.Core.Exceptions;
using Ivy.Tendril.Agents;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Ivy.Tendril;

internal static class ServiceRegistration
{
    public static void AddTendrilServices(this Server server, ConfigService configService, TendrilArgs? tendrilArgs = null)
    {
        server.Services.AddHttpClient();
        server.Services.AddSingleton<IExceptionHandler>(sp =>
            new LoggingExceptionHandler(sp.GetRequiredService<ILogger<LoggingExceptionHandler>>()));

        server.Services.AddSingleton<IConfigService>(configService);
        server.Services.AddSingleton<ConfigService>(configService);
        server.Services.AddSingleton<ICreatePlanPreferences, CreatePlanPreferences>();

        Program.SetConfigServiceForCleanup(configService);

        server.Services.AddHttpContextAccessor();

        if (configService.Settings.Auth != null)
            server.UseAuth<Auth.TendrilAuthProvider>();

        server.Services.AddAgentInfrastructure(opts =>
        {
            opts.IncludeBetaProviders = tendrilArgs?.Beta ?? false;
        });

        server.Services.AddSingleton<ModelPricingService>();

        if (configService.Settings.Llm is { } llmConfig && !string.IsNullOrEmpty(llmConfig.ApiKey))
            server.Services.AddSingleton<IChatClient>(sp =>
            {
                var config = sp.GetRequiredService<IConfigService>();
                var llm = config.Settings.Llm!;
                var endpoint = !string.IsNullOrEmpty(llm.Endpoint) ? llm.Endpoint : "https://api.openai.com/v1";
                var client = new OpenAIClient(
                    new ApiKeyCredential(llm.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
                return client.GetChatClient(llm.Model).AsIChatClient();
            });

        server.Services.AddSingleton<VersionCheckService>();
        server.Services.AddSingleton<IVersionCheckService>(sp => sp.GetRequiredService<VersionCheckService>());
        server.Services.AddSingleton<IPromptwareRunner, PromptwareRunner>();

        server.Services.AddSingleton<OnboardingSetupService>();
        server.Services.AddSingleton<IOnboardingSetupService>(sp => sp.GetRequiredService<OnboardingSetupService>());
        server.Services.AddSingleton<GithubService>();
        server.Services.AddSingleton<IGithubService>(sp => sp.GetRequiredService<GithubService>());
        server.Services.AddSingleton<IGitService>(sp =>
            new GitService(
                sp.GetRequiredService<IConfigService>(),
                sp.GetRequiredService<ILogger<GitService>>()));
        server.Services.AddSingleton<IWorktreeLifecycleLogger>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var home = string.IsNullOrEmpty(config.TendrilHome)
                ? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".tendril")
                : config.TendrilHome;
            return new WorktreeLifecycleLogger(home);
        });
        server.Services.AddSingleton<PlanReaderService>(sp =>
        {
            var planService = new PlanReaderService(
                sp.GetRequiredService<IConfigService>(),
                sp.GetRequiredService<ILogger<PlanReaderService>>(),
                sp.GetRequiredService<ITelemetryService>(),
                sp.GetRequiredService<IWorktreeLifecycleLogger>());
            planService.MigratePlanSubfolderCasing();
            planService.RepairPlans();
            planService.RecoverStuckPlans();
            return planService;
        });
        server.Services.AddSingleton<IPlanReaderService>(sp => sp.GetRequiredService<PlanReaderService>());

        server.Services.AddSingleton<IPlanDatabaseService>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfigService>();
            if (string.IsNullOrEmpty(cfg.TendrilHome))
                throw new InvalidOperationException("Cannot create PlanDatabaseService: TendrilHome is not configured. Complete onboarding first.");
            var dbPath = Path.Combine(cfg.TendrilHome, "tendril.db");
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<PlanDatabaseService>();
            return new PlanDatabaseService(dbPath, logger);
        });
        server.Services.AddSingleton<PlanDatabaseSyncService>(sp =>
        {
            var planReader = sp.GetRequiredService<PlanReaderService>();
            var database = sp.GetRequiredService<IPlanDatabaseService>();
            var watcher = sp.GetRequiredService<IPlanWatcherService>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new PlanDatabaseSyncService(planReader, database, watcher,
                loggerFactory.CreateLogger<PlanDatabaseSyncService>());
        });
        server.Services.AddSingleton<ITelemetryService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var logger = sp.GetRequiredService<ILogger<TelemetryService>>();
            return new TelemetryService(config.Settings.Telemetry, logger);
        });
        server.Services.AddSingleton<TelemetryService>(sp =>
            (TelemetryService)sp.GetRequiredService<ITelemetryService>());
        server.Services.AddSingleton<JobService>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfigService>();
            return new JobService(
                cfg,
                sp.GetRequiredService<ILogger<JobService>>(),
                sp.GetRequiredService<ModelPricingService>(),
                sp.GetRequiredService<IPlanReaderService>(),
                sp.GetRequiredService<ITelemetryService>(),
                sp.GetRequiredService<IPlanWatcherService>(),
                string.IsNullOrEmpty(cfg.TendrilHome) ? null : sp.GetRequiredService<IPlanDatabaseService>(),
                sp.GetRequiredService<IAgentRunner>());
        });
        server.Services.AddSingleton<IJobService>(sp => sp.GetRequiredService<JobService>());
        server.Services.AddSingleton<PlanWatcherService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var logger = sp.GetService<ILogger<PlanWatcherService>>();
            return new PlanWatcherService(config, logger);
        });
        server.Services.AddSingleton<IPlanWatcherService>(sp => sp.GetRequiredService<PlanWatcherService>());
        server.Services.AddSingleton<TendrilProcessStatusService>(sp =>
        {
            var planReader = sp.GetRequiredService<IPlanReaderService>();
            var jobService = sp.GetRequiredService<IJobService>();
            var planWatcher = sp.GetRequiredService<IPlanWatcherService>();
            var config = sp.GetRequiredService<IConfigService>();
            var logger = sp.GetRequiredService<ILogger<TendrilProcessStatusService>>();
            return new TendrilProcessStatusService(planReader, jobService, planWatcher, config, logger);
        });
        server.Services.AddSingleton<ITendrilProcessStatusService>(sp => sp.GetRequiredService<TendrilProcessStatusService>());
        server.Services.AddSingleton<InboxWatcherService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var jobService = sp.GetRequiredService<IJobService>();
            return new InboxWatcherService(config, jobService, sp.GetRequiredService<ILogger<InboxWatcherService>>());
        });
        server.Services.AddSingleton<IInboxWatcherService>(sp => sp.GetRequiredService<InboxWatcherService>());
        server.Services.AddSingleton<WorktreeCleanupService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var logger = sp.GetRequiredService<ILogger<WorktreeCleanupService>>();
            var lifecycleLogger = sp.GetRequiredService<IWorktreeLifecycleLogger>();
            return new WorktreeCleanupService(config.PlanFolder, logger, lifecycleLogger,
                terminalGrace: TimeSpan.FromMinutes(config.Settings.WorktreeTerminalGraceMinutes),
                staleReaperPeriod: TimeSpan.FromDays(config.Settings.WorktreeStaleReaperDays),
                timerInterval: TimeSpan.FromMinutes(config.Settings.WorktreeCleanupIntervalMinutes));
        });
        server.Services.AddSingleton<IStartable>(sp => sp.GetRequiredService<WorktreeCleanupService>());
        server.Services.AddSingleton<PrStatusSyncService>(sp =>
        {
            var database = sp.GetRequiredService<IPlanDatabaseService>();
            var githubService = sp.GetRequiredService<IGithubService>();
            var planReader = sp.GetRequiredService<IPlanReaderService>();
            var logger = sp.GetRequiredService<ILogger<PrStatusSyncService>>();
            return new PrStatusSyncService(database, githubService, planReader, logger);
        });
        server.Services.AddSingleton<IStartable>(sp => sp.GetRequiredService<PrStatusSyncService>());

        server.Services.AddSingleton<MasterElectionService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var appLifetime = sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
            var svr = sp.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            var logger = sp.GetRequiredService<ILogger<MasterElectionService>>();
            return new MasterElectionService(config, appLifetime, svr, logger);
        });
        server.Services.AddSingleton<IMasterElectionService>(sp => sp.GetRequiredService<MasterElectionService>());
        server.Services.AddSingleton<IStartable>(sp => sp.GetRequiredService<MasterElectionService>());

        server.Services.AddSingleton<Services.Tunnel.CloudflaredService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var appLifetime = sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
            var svr = sp.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            var logger = sp.GetRequiredService<ILogger<Services.Tunnel.CloudflaredService>>();
            return new Services.Tunnel.CloudflaredService(config, httpFactory, appLifetime, svr, logger);
        });
        server.Services.AddSingleton<Services.Tunnel.ICloudflaredService>(sp =>
            sp.GetRequiredService<Services.Tunnel.CloudflaredService>());
        server.Services.AddSingleton<IStartable>(sp =>
            sp.GetRequiredService<Services.Tunnel.CloudflaredService>());
    }
}
