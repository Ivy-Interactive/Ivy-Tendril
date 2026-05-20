using System.ClientModel;
using Ivy.Core.Exceptions;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.SessionParsers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Ivy.Tendril;

internal static class ServiceRegistration
{
    public static void AddTendrilServices(this Server server, ConfigService configService)
    {
        server.Services.AddHttpClient();
        server.Services.AddSingleton<IExceptionHandler>(sp =>
            new LoggingExceptionHandler(sp.GetRequiredService<ILogger<LoggingExceptionHandler>>()));

        server.Services.AddSingleton<IConfigService>(configService);
        server.Services.AddSingleton<ConfigService>(configService);

        Program.SetConfigServiceForCleanup(configService);

        server.Services.AddHttpContextAccessor();

        if (configService.Settings.Auth != null)
            server.UseAuth<Auth.TendrilAuthProvider>();

        server.Services.AddSingleton<ISessionParser, ClaudeSessionParser>();
        server.Services.AddSingleton<ISessionParser, CodexSessionParser>();
        server.Services.AddSingleton<ISessionParser, GeminiSessionParser>();
        server.Services.AddSingleton<ModelPricingService>();
        server.Services.AddSingleton<IModelPricingService>(sp => sp.GetRequiredService<ModelPricingService>());

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
        server.Services.AddSingleton<IOnboardingAuthRunner, OnboardingAuthRunner>();
        server.Services.AddSingleton<GithubService>();
        server.Services.AddSingleton<IGithubService>(sp => sp.GetRequiredService<GithubService>());
        server.Services.AddSingleton<IGitService>(sp =>
            new GitService(
                sp.GetRequiredService<IConfigService>(),
                sp.GetRequiredService<ILogger<GitService>>()));
        server.Services.AddSingleton<IWorktreeLifecycleLogger>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            return new WorktreeLifecycleLogger(
                string.IsNullOrEmpty(config.TendrilHome) ? "." : config.TendrilHome);
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
                sp.GetRequiredService<IWorktreeLifecycleLogger>());
        });
        server.Services.AddSingleton<IJobService>(sp => sp.GetRequiredService<JobService>());
        server.Services.AddSingleton<PlanWatcherService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var logger = sp.GetService<ILogger<PlanWatcherService>>();
            return new PlanWatcherService(config, logger);
        });
        server.Services.AddSingleton<IPlanWatcherService>(sp => sp.GetRequiredService<PlanWatcherService>());
        server.Services.AddSingleton<PlanCountsService>(sp =>
        {
            var planReader = sp.GetRequiredService<IPlanReaderService>();
            var jobService = sp.GetRequiredService<IJobService>();
            var planWatcher = sp.GetRequiredService<IPlanWatcherService>();
            return new PlanCountsService(planReader, jobService, planWatcher);
        });
        server.Services.AddSingleton<IPlanCountsService>(sp => sp.GetRequiredService<PlanCountsService>());
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
            return new WorktreeCleanupService(config.PlanFolder, logger, lifecycleLogger);
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
    }
}
