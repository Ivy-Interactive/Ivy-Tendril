using Ivy.Core.Apps;
using Ivy.Helpers;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Apps.ReviewAction;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Controllers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Widgets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

        // A review action turns into a WebViewer once the app it started prints its URL
        // (Apps/ReviewAction), and that widget proxies the app through this origin. Its
        // endpoints have to be served here and kept out of the app router.
        server.ReservePaths(WebViewerProxy.ReservedPaths);

        var configService = new ConfigService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigService>.Instance);
        server.Services.AddSingleton(tendrilArgs);
        server.AddTendrilServices(configService, tendrilArgs);

        var defaultLogLevel = tendrilArgs.Verbose ? "Debug"
            : tendrilArgs.Quiet ? "Warning"
            : "Warning";
        var appLogLevel = tendrilArgs.Verbose ? "Debug"
            : tendrilArgs.Quiet ? "Warning"
            : "Information";

        var isBeta = BetaHelper.IsBeta(tendrilArgs, configService);

        server.UseWebApplicationBuilder(builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = defaultLogLevel,
                ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
                ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Information",
                ["Logging:LogLevel:Ivy"] = appLogLevel,
                ["Logging:LogLevel:Ivy.Core"] = "Warning",
            });

            builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
            {
                options.MaximumReceiveMessageSize = 100 * 1024 * 1024;
            });

            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 100 * 1024 * 1024;
            });
        });

        server.UseWebApplication(app =>
        {
            if (isBeta)
            {
                app.Use(async (context, next) =>
                {
                    if (context.Request.Query.ContainsKey("share") ||
                        string.Equals(context.Request.Query["mode"], "share", StringComparison.OrdinalIgnoreCase) ||
                        context.Request.Headers.ContainsKey("X-Tendril-Share") ||
                        context.Request.Cookies.ContainsKey("tendril_share_mode"))
                    {
                        context.Items["IsShareMode"] = true;

                        if (!context.Request.Cookies.ContainsKey("tendril_share_mode"))
                        {
                            context.Response.Cookies.Append("tendril_share_mode", "1", new CookieOptions
                            {
                                HttpOnly = false,
                                SameSite = SameSiteMode.Lax,
                                Path = "/",
                                MaxAge = TimeSpan.FromDays(7)
                            });
                        }
                    }
                    await next(context);
                });
            }

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

                _ = BackgroundServiceActivator.StartAsync(app.Services, app.Services.GetRequiredService<ILogger<Server>>());
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

            // Fetches whatever URL its caller names, so it is held to the app being reviewed:
            // this machine, or the network it is on. Without a predicate it is an open relay,
            // and Tendril's origin is reachable by anyone the user shares a tunnel with.
            app.MapWebViewerProxy(new WebViewerProxyOptions { IsUrlAllowed = AppPreview.IsAllowedTarget });
        });

        var assembly = typeof(TendrilServer).Assembly;
        server.AppRepository.AddFactory(() => AppHelpers.GetApps(assembly)
            .Select(app => app.Type == typeof(Ivy.Tendril.Apps.Chat.ChatApp) ? new AppDescriptor
            {
                Id = app.Id,
                Title = app.Title,
                Icon = app.Icon,
                Description = app.Description,
                Type = app.Type,
                Group = app.Group,
                Order = app.Order,
                ViewFactory = app.ViewFactory,
                ViewFunc = app.ViewFunc,
                IsVisible = isBeta,
                IsIndex = app.IsIndex,
                GroupExpanded = app.GroupExpanded,
                Next = app.Next,
                Previous = app.Previous,
                DocumentSource = app.DocumentSource,
                SearchHints = app.SearchHints,
                AllowDuplicateTabs = app.AllowDuplicateTabs,
            } : app)
            .ToArray());
        server.AddConnectionsFromAssembly(typeof(TendrilServer).Assembly);

        // Eagerly register Ivy.Tendril.Widgets and framework widgets assemblies to ensure widgets
        // are discovered when running in single-file published mode (where DLLs are not on disk)
        Ivy.Core.ExternalWidgets.ExternalWidgetRegistry.Instance.RegisterAssembly(
            typeof(TendrilProcessViewer).Assembly);
        Ivy.Core.ExternalWidgets.ExternalWidgetRegistry.Instance.RegisterAssembly(
            typeof(Ivy.Widgets.Xterm.Terminal).Assembly);
        Ivy.Core.ExternalWidgets.ExternalWidgetRegistry.Instance.RegisterAssembly(
            typeof(Ivy.Widgets.DiffView.DiffView).Assembly);
        Ivy.Core.ExternalWidgets.ExternalWidgetRegistry.Instance.RegisterAssembly(
            typeof(Ivy.Widgets.QRCode.QRCode).Assembly);
        Ivy.Core.ExternalWidgets.ExternalWidgetRegistry.Instance.RegisterAssembly(
            typeof(Ivy.Widgets.ActivityHeatmap.ActivityHeatmap).Assembly);
        Ivy.Core.ExternalWidgets.ExternalWidgetRegistry.Instance.RegisterAssembly(
            typeof(Ivy.Widgets.AnimatedStatusLabel.AnimatedStatusLabel).Assembly);

        var version = typeof(TendrilAppShell).Assembly.GetName().Version!;
        var versionString = version.ToString(3);
        var appShellSettings = new AppShellSettings()
            .DefaultApp<PlansApp>()
            .Header(
                Layout.Horizontal(
                    new Image("/tendril/assets/Tendril.svg").Width(Size.Px(32)).Height(Size.Px(32)),
                    Layout.Vertical(
                        Text.Block("Ivy Tendril").NoWrap(),
                        Text.Muted($"v{versionString}").NoWrap()
                    ).Gap(0)
                ).Gap(2).Padding(2).AlignContent(Align.Left)
            )
            .HideArgsInUrl()
            .UseTabs(true);

        server.UseAppShell(() => new TendrilAppShell(appShellSettings));

        return server;
    }
}
