using Ivy.Helpers;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Controllers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril;

public static class TendrilServer
{
    public static Server Create(string[] args)
    {
        var server = new Server();
        server.DangerouslyAllowLocalFiles();
        server.UseCulture("en-US");
#if DEBUG
        server.UseHotReload();
#endif
        server.SetMetaTitle("Ivy Tendril");

        var configService = new ConfigService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigService>.Instance);
        server.AddTendrilServices(configService);

        // Configure logging based on verbosity.
        // We set levels via configuration (not SetMinimumLevel) because the Ivy framework
        // calls SetMinimumLevel after UseWebApplicationBuilder mods, which would override ours.
        // Configuration-based rules take precedence over SetMinimumLevel.
        var logLevel = Environment.GetEnvironmentVariable("TENDRIL_VERBOSE") == "1" ? "Debug"
            : Environment.GetEnvironmentVariable("TENDRIL_QUIET") == "1" ? "Warning"
            : "Error";
        server.UseWebApplicationBuilder(builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = logLevel,
                ["Logging:LogLevel:Ivy.Core"] = "Warning",
            });
        });

        server.UseWebApplication(app =>
        {
            app.UseMiddleware<ApiKeyAuthMiddleware>();

            // Publish the actual bound URL so child processes can reach this server
            var serverUrl = app.Urls.FirstOrDefault();
            if (serverUrl != null)
                Environment.SetEnvironmentVariable("TENDRIL_URL", serverUrl);

            if (!configService.NeedsOnboarding)
            {
                // Auto-update promptwares if the running version is newer than what's deployed
                var promptwaresDir = Path.Combine(configService.TendrilHome, "Promptwares");
                PromptwareDeployer.CleanupOrphanedPreservedDirectories(promptwaresDir);
                if (PromptwareDeployer.NeedsUpdate(promptwaresDir))
                {
                    var logger = app.Services.GetRequiredService<ILogger<Server>>();
                    logger.LogInformation("Promptware update detected, deploying new version");
                    PromptwareDeployer.Deploy(promptwaresDir);
                }

                BackgroundServiceActivator.Start(app.Services);
            }

            var telemetryService = app.Services.GetRequiredService<TelemetryService>();
            var appVersion = typeof(TendrilAppShell).Assembly.GetName().Version!.ToString(3);
            telemetryService.TrackAppStarted(new AppStartContext(
                appVersion,
                configService.Settings.Projects.Count,
                configService.Settings.Llm?.ApiKey != null));
            _ = Task.Run(async () =>
            {
                try
                {
                    await telemetryService.IdentifyAsync(appVersion);
                    await telemetryService.FlushAsync();
                }
                catch (Exception ex)
                {
                    CrashLog.Write($"[{DateTime.UtcNow:O}] Telemetry startup exception: {ex}");
                }
            });
            app.UseAssets(server.Args, app.Services.GetRequiredService<ILogger<Server>>(), "Assets", "tendril/assets");
        });

        server.AddAppsFromAssembly(typeof(TendrilServer).Assembly);
        server.AddConnectionsFromAssembly(typeof(TendrilServer).Assembly);

        var version = typeof(TendrilAppShell).Assembly.GetName().Version!;
        var versionString = version.ToString(3);
        var appShellSettings = new AppShellSettings()
            .Header(
                Layout.Horizontal(
                    new Image("/tendril/assets/Tendril.svg").Width(Size.Units(15)).Height(Size.Auto()),
                    Layout.Vertical(
                        Text.Block("Ivy Tendril"),
                        Text.Muted($"v{versionString}")
                    ).Gap(0)
                ).Gap(2).Padding(2).AlignContent(Align.BottomLeft)
            )
            .WallpaperApp<Apps.WallpaperApp>()
            .UseTabs(true);

        server.UseAppShell(() => new TendrilAppShell(appShellSettings));

        return server;
    }
}