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
        var configService = new ConfigService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigService>.Instance);

        ConfigureLocalFileAccess(server, configService);

        server.UseCulture("en-US");
#if DEBUG
        server.UseHotReload();
#endif
        server.SetMetaTitle(AppBrand.AppName);

        // A review action turns into a WebViewer once the app it started prints its URL
        // (Apps/ReviewAction), and that widget proxies the app through this origin. Its
        // endpoints have to be served here and kept out of the app router.
        server.ReservePaths(WebViewerProxy.ReservedPaths);

        // Plan wireframes are served live from this origin (see WireframeHost), so a preview
        // works over HTTPS and through a share tunnel exactly like the page framing it.
        server.ReservePaths(Ivy.Tendril.Wireframe.Hosting.WireframeEndpoints.ReservedPaths);
        server.Services.AddSingleton(sp => new Ivy.Tendril.Wireframe.Hosting.WireframeHost(
            Ivy.Tendril.Wireframe.Assets.AssetCatalog.Default,
            (scope, name) =>
            {
                // Resolved per request: the plans folder is configuration, and can change.
                var plans = sp.GetService<Ivy.Tendril.Services.Plans.IPlanReaderService>();
                return Ivy.Tendril.Services.Wireframes.PlanWireframes.ResolveRoot(plans?.PlansDirectory, scope, name);
            }));

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

            app.UseMiddleware<LocalFileGuardMiddleware>();
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

            // Live plan wireframes: the shared payload once, then one route per plan wireframe.
            app.UseWebSockets();
            Ivy.Tendril.Wireframe.Hosting.WireframeEndpoints.MapWireframePayload(app,
                Ivy.Tendril.Wireframe.Assets.AssetCatalog.Default);
            Ivy.Tendril.Wireframe.Hosting.WireframeEndpoints.MapWireframeSite(app,
                Ivy.Tendril.Wireframe.Hosting.WireframeHost.RoutePattern,
                Ivy.Tendril.Wireframe.Assets.AssetCatalog.Default,
                app.Services.GetRequiredService<Ivy.Tendril.Wireframe.Hosting.WireframeHost>());
        });

        var assembly = typeof(TendrilServer).Assembly;
        server.AppRepository.AddFactory(() => AppHelpers.GetApps(assembly).ToArray());
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
                        Text.Block(AppBrand.AppName).NoWrap(),
                        Text.Muted($"v{versionString}").NoWrap()
                    ).Gap(0)
                ).Gap(2).Padding(2).AlignContent(Align.Left)
            )
            .HideArgsInUrl()
            .UseTabs(true);

        server.UseAppShell(() => new TendrilAppShell(appShellSettings));

        return server;
    }

    internal static void ConfigureLocalFileAccess(Server server, IConfigService configService)
    {
        var roots = GetAllowedRoots(configService);
        var dangerouslyAllowWithRoots = typeof(Server).GetMethod("DangerouslyAllowLocalFiles", [typeof(string[])]);
        if (dangerouslyAllowWithRoots != null)
        {
            dangerouslyAllowWithRoots.Invoke(server, new object[] { roots });
        }
        else
        {
            server.DangerouslyAllowLocalFiles();
        }

        var allowExtensions = typeof(Server).GetMethod("AllowLocalFileExtensions", [typeof(string[])]);
        if (allowExtensions != null)
        {
            allowExtensions.Invoke(server, new object[] { LocalFileGuardMiddleware.AllowedFileExtensions });
        }

        configService.SettingsReloaded += (_, _) =>
        {
            RefreshRoots(server, configService);
        };
    }

    internal static string[] GetAllowedRoots(IConfigService configService)
    {
        var roots = LocalFileRootPolicy.ComputeRoots(configService);
        if (roots.Count > 0)
        {
            return roots.ToArray();
        }

        if (!string.IsNullOrWhiteSpace(configService.TendrilHome))
        {
            return [configService.TendrilHome];
        }

        return [];
    }

    internal static void RefreshRoots(Server server, IConfigService configService)
    {
        var roots = GetAllowedRoots(configService);
        var rootsProp = server.Args?.GetType().GetProperty("LocalFileRoots");
        if (rootsProp != null && rootsProp.CanWrite)
        {
            rootsProp.SetValue(server.Args, roots);
        }
        else
        {
            var dangerouslyAllowWithRoots = typeof(Server).GetMethod("DangerouslyAllowLocalFiles", [typeof(string[])]);
            dangerouslyAllowWithRoots?.Invoke(server, new object[] { roots });
        }
    }
}
