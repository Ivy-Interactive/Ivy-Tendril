using Ivy.Core.Apps;
using Ivy.Helpers;
using Ivy.Tendril.Apps;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Controllers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Widgets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril;

public static class TendrilServer
{
    public static Server Create(string[] args, TendrilArgs tendrilArgs)
    {
        PathHelper.AugmentPath(forceShellPath: true);
        var server = new Server();
        server.DangerouslyAllowLocalFiles();
        server.UseCulture("en-US");
#if DEBUG
        server.UseHotReload();
#endif
        server.SetMetaTitle("Ivy Tendril");

        var configService = new ConfigService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigService>.Instance);
        server.Services.AddSingleton(tendrilArgs);
        server.AddTendrilServices(configService, tendrilArgs);

        var logLevel = tendrilArgs.Verbose ? "Debug"
            : tendrilArgs.Quiet ? "Warning"
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

        var assembly = typeof(TendrilServer).Assembly;
        server.AppRepository.AddFactory(() => AppHelpers.GetApps(assembly)
            .ToArray());
        server.AddConnectionsFromAssembly(typeof(TendrilServer).Assembly);

        // Load plugins from the plugins directory under Tendril root
        if (!string.IsNullOrEmpty(configService.TendrilHome) && Directory.Exists(configService.TendrilHome))
        {
            var pluginsDir = Path.Combine(configService.TendrilHome, "plugins");
            Directory.CreateDirectory(pluginsDir);
            TendrilPluginContext? tendrilPluginContext = null;
            server.UsePlugins(pluginsDir,
                new TendrilPluginConfigFactory(pluginsDir),
                contextFactory: (s, builder) =>
                {
                    tendrilPluginContext = new TendrilPluginContext(s, builder, configService.TendrilHome,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<TendrilPluginContext>.Instance);
                    builder.Services.AddSingleton<AppShell.ITendrilPluginContributions>(tendrilPluginContext);
                    builder.Services.AddSingleton(tendrilPluginContext.HookRegistry);
                    configService.PluginHooks = tendrilPluginContext.HookRegistry;
                    return tendrilPluginContext;
                },
                sharedAssemblyNames: ["Ivy.Tendril.Plugin.Abstractions", "Ivy.Tendril.Plugin.Extended.Abstractions"],
                buildSourcePlugins: true);
        }

        // Eagerly register Ivy.Tendril.Widgets assembly to ensure Tendril widgets are discovered
        // when running in single-file published mode (where DLLs are not on disk)
        Ivy.Core.ExternalWidgets.ExternalWidgetRegistry.Instance.RegisterAssembly(
            typeof(TendrilProcessViewer).Assembly);

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
                ).Gap(2).Padding(2).AlignContent(Align.BottomLeft).Height(Size.Auto())
            )
            .WallpaperApp<WallpaperApp>()
            .HideArgsInUrl()
            .UseTabs(true);

        server.UseAppShell(() => new TendrilAppShell(appShellSettings));

        return server;
    }
}
