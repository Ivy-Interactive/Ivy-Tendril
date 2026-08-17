using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Text.Json;
using Ivy.Core;
using Ivy.Core.Apps;
using Ivy.Desktop;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.AppShell.Dialogs;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.ReviewAction;
using Ivy.Tendril.Apps.Settings;
using Ivy.Tendril.Apps.Trash;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Widgets.Internal;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.AppShell;

#pragma warning disable IVYAPP001

public class TendrilAppShell(AppShellSettings settings) : ViewBase
{
    internal AppShellSettings Settings => settings;

    private static readonly HttpClient NewsHttp = new();
    private static readonly HashSet<string> OnboardingAppIds =
        new(StringComparer.OrdinalIgnoreCase) { "onboarding", "OnboardingApp", "onboarding-app" };

    /// <summary>
    ///     Overrides the default app-descriptor tab title for apps whose title depends on the
    ///     navigation args rather than being fixed. <see cref="ReviewActionApp"/> is titled
    ///     "#&lt;PlanId&gt; &lt;ReviewActionName&gt;", formatted here from the plan folder name.
    ///     <see cref="AgentApp"/> is titled with the caller-supplied <see cref="AgentAppArgs.Title"/>
    ///     verbatim (see the Draft/Review "Discuss with &lt;Agent&gt;" call sites, which build it
    ///     with <see cref="FormatPlanId"/>). Returns null (use the descriptor title) for every other
    ///     arg shape, including <see cref="AgentAppArgs"/> without a <see cref="AgentAppArgs.Title"/>.
    /// </summary>
    internal static string? ResolveArgsTabTitle(object? appArgs) => appArgs switch
    {
        ReviewActionAppArgs { PlanId: { Length: > 0 } planId, ActionName: { Length: > 0 } name }
            => $"#{FormatPlanId(planId)} {name}",
        AgentAppArgs { Title: { Length: > 0 } title } => title,
        _ => null
    };

    // "00074-OpenReviewActions..." -> "74". Falls back to the raw folder name when the numeric
    // prefix is missing, so a malformed arg never throws inside OpenApp (which swallows exceptions).
    internal static string FormatPlanId(string planFolderName) =>
        int.TryParse(PlanYamlHelper.ExtractPlanIdFromFolder(planFolderName), out var id)
            ? id.ToString()
            : planFolderName;

    /// <summary>
    ///     Whether the in-app toast should be shown for a job notification. In desktop mode the
    ///     native OS notification raised in Program.cs covers it, so the toast would be a duplicate.
    ///     When desktop notifications are switched off the native path does not fire, so the toast
    ///     is the only notification left and must stay.
    /// </summary>
    internal static bool ShouldShowInAppToast(bool isDesktop, bool desktopNotificationsEnabled)
        => !isDesktop || !desktopNotificationsEnabled;

    /// <summary>
    ///     Whether Cmd+W / Ctrl+W should be wired up to close the active tab. Desktop shell only: in a
    ///     browser the chord belongs to the browser (it closes the browser tab and cannot be cancelled),
    ///     and <see cref="AppShellNavigation.Pages"/> has no tab strip at all.
    /// </summary>
    internal static bool ShouldEnableCloseTabShortcut(
        bool isDesktop, AppShellNavigation navigation, int tabCount, int? selectedIndex)
        => isDesktop
           && navigation != AppShellNavigation.Pages
           && selectedIndex is { } index
           && index >= 0
           && index < tabCount;

    private static async Task<SidebarNewsArticle[]> FetchNewsAsync()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TENDRIL_E2E")))
            return [];

        try
        {
            var json = await NewsHttp.GetStringAsync(Constants.NewsBaseUrl + "news.json");
            var items = JsonSerializer.Deserialize<JsonElement[]>(json) ?? [];
            return items.Select(e =>
            {
                var id = e.GetProperty("id").GetString() ?? "";
                var href = e.GetProperty("href").GetString() ?? "";
                var title = e.GetProperty("title").GetString() ?? "";
                var summary = e.GetProperty("summary").GetString() ?? "";
                var image = e.GetProperty("image").GetString() ?? "";
                if (!image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    image = Constants.NewsBaseUrl + image;
                return new SidebarNewsArticle(id, href, title, summary, image);
            }).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool ShouldShowBadge(MenuItem item, Dictionary<string, int> badges, out string badgeText)
    {
        badgeText = string.Empty;
        if (item.Tag is string tag && badges.TryGetValue(tag, out var count) && count > 0)
        {
            badgeText = count.ToString();
            return true;
        }
        return false;
    }

    private static MenuItem AddBadge(MenuItem item, Dictionary<string, int> badges)
    {
        if (ShouldShowBadge(item, badges, out var badgeText))
            item = item.Badge(badgeText);
        if (item.Children is { Length: > 0 })
            item = item with { Children = item.Children.Select(c => AddBadge(c, badges)).ToArray() };
        return item;
    }

    // The Agent app id (and its menu-item Tag) collapses to "agent" via AppHelpers.GetApp.
    private const string AgentAppId = "agent";

    // Re-brand the generic "Agent" menu item to the configured coding agent (e.g. "Claude Code"
    // with the Claude icon), so the sidebar matches what actually launches.
    private static MenuItem BrandAgentItem(MenuItem item, string agentId, IAgentRunner runner, IConfigService config)
    {
        if (item.Tag is string tag && tag == AgentAppId)
        {
            var (label, icon) = AgentBranding.For(agentId, runner, config);
            item = item.Label(label).Icon(icon);
        }
        if (item.Children is { Length: > 0 })
            item = item with { Children = item.Children.Select(c => BrandAgentItem(c, agentId, runner, config)).ToArray() };
        return item;
    }

    private static MenuItem[] BuildMenuItems(IAppRepository repo, TendrilProcessStatus status,
        IConfigService config, IAgentRunner runner)
    {
        var nonChatAgentCount = Math.Max(0, runner.ActiveSessions.Count - status.GeneratingChatSessionsCount);
        var badges = new Dictionary<string, int>
        {
            ["drafts"] = status.DraftCount,
            ["review"] = status.ReviewCount,
            ["jobs"] = status.JobCount,
            ["icebox"] = status.IceboxCount,
            ["recommendations"] = status.RecommendationsCount,
            ["trash"] = status.TrashCount,
            ["chat"] = status.GeneratingChatSessionsCount,
            ["agent"] = nonChatAgentCount
        };
        var agentId = config.Settings.CodingAgent;
        return repo.GetMenuItems()
            .Select(m => AddBadge(m, badges))
            .Select(m => BrandAgentItem(m, agentId, runner, config))
            .ToArray();
    }

    public override object Build()
    {
        // All hooks must be at the top level of Build()
        var config = UseService<IConfigService>();
        var logger = UseService<ILogger<TendrilAppShell>>();
        var tabs = UseState(ImmutableArray.Create<TabState>);
        var selectedIndex = UseState<int?>();
        var appRepository = UseService<IAppRepository>();
        var client = UseService<IClientProvider>();
        var versionService = UseService<IVersionCheckService>();
        var currentApp = UseState<AppHost?>();
        var statusService = UseService<ITendrilProcessStatusService>();
        var agentRunner = UseService<IAgentRunner>();
        var menuItems = UseState(() => BuildMenuItems(appRepository, statusService.Current, config, agentRunner));
        var status = UseState(() => statusService.Current);
        var sidebarOpen = UseState(config.Settings.SidebarOpen);
        var args = UseService<AppContext>();
        var serverArgs = UseService<ServerArgs>();
        var navigate = Context.UseSignal<NavigateSignal, NavigateArgs, Unit>();
        var navigator = UseNavigation();
        var newsArticles = UseState(Array.Empty<SidebarNewsArticle>());

        var (importIssuesDialog, showImportIssuesDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new ImportIssuesDialog(isOpen, config);
        });

        var (updateDialog, showUpdateDialog) = UseTrigger<VersionInfo>((isOpen, info) =>
        {
            if (!isOpen.Value || info == null) return null;
            return new UpdateTendrilDialog(isOpen, info);
        });

        UseEffect(async () =>
        {
            newsArticles.Set(await FetchNewsAsync());
        });

        UseEffect(() =>
        {
            var subscription = statusService.Status.Subscribe(s => status.Set(s));
            return Disposable.Create(() => subscription.Dispose());
        });

        UseEffect(() =>
        {
            return navigate.Receive(navigateArgs =>
            {
                OpenApp(navigateArgs);
                return default!;
            });
        });

        UseEffect(() => { menuItems.Set(BuildMenuItems(appRepository, status.Value, config, agentRunner)); },
            appRepository.Reloaded.ToTrigger(), status);

        // Rebuild the menu when settings are saved (e.g. the coding agent changes), so the
        // branded "Agent" item updates immediately without needing a reload.
        UseEffect(() =>
        {
            void OnSettingsReloaded(object? sender, EventArgs e)
            {
                menuItems.Set(BuildMenuItems(appRepository, status.Value, config, agentRunner));
                sidebarOpen.Set(config.Settings.SidebarOpen);
            }
            config.SettingsReloaded += OnSettingsReloaded;
            return Disposable.Create(() => config.SettingsReloaded -= OnSettingsReloaded);
        });

        var jobService = UseService<IJobService>();
        Context.TryUseService<DesktopWindow>(out var desktopWindow);
        var isDesktop = desktopWindow != null;

        UseEffect(() =>
        {
            void OnNotification(JobNotification notification)
            {
                // Read the setting at notification time: the user can toggle it in Settings while
                // the shell is mounted, and the effect does not re-subscribe on that change.
                if (!ShouldShowInAppToast(isDesktop, config.Settings.DesktopNotifications))
                    return;

                if (notification.IsSuccess)
                    client.Toast(notification.Message, notification.Title);
                else
                    client.Toast(notification.Message, notification.Title).Destructive();
            }

            jobService.NotificationReady += OnNotification;
            return Disposable.Create(() => jobService.NotificationReady -= OnNotification);
        });

        UseEffect(async () =>
        {
            if (config.NeedsOnboarding) return;

            var initialAppId = args.NavigationAppId ?? settings.DefaultAppId;
            var targetAppId = initialAppId;
            if (!string.IsNullOrWhiteSpace(targetAppId))
            {
                // Force redirect from onboarding if it's already done
                if (!config.NeedsOnboarding && OnboardingAppIds.Contains(targetAppId))
                    targetAppId = settings.DefaultAppId;

                var appArgs = args.GetArgs<object>();
                OpenApp(new NavigateArgs(targetAppId, appArgs), true);
            }
            else
            {
                client.Redirect("/", true);
            }
        });

        // Auto-default: if there's exactly one visible app, select it and close sidebar
        var visibleApps = appRepository.GetMenuItems().FlattenWithPath().ToArray();
        if (visibleApps is [{ Item.Tag: string singleAppId } _])
            settings = settings with
            {
                DefaultAppId = settings.DefaultAppId ?? singleAppId,
                SidebarOpen = false
            };

        // The Agent app's descriptor title/icon are the generic "Agent"/terminal; brand them
        // to the configured coding agent so tabs and the browser title stay consistent.
        (string Title, Icons? Icon) BrandedAppDisplay(AppDescriptor app)
        {
            if (app.Id == AgentAppId)
            {
                var (label, icon) = AgentBranding.For(config.Settings.CodingAgent, agentRunner, config);
                return (label, icon);
            }
            return (app.Title, app.Icon);
        }

        void SetAppTitle(string appId)
        {
            var app = appRepository.GetAppOrDefault(appId);
            var (title, _) = BrandedAppDisplay(app);
            if (title is { } t) client.SetTitle(t, serverArgs.Metadata.Title);
        }

        bool IsErrorApp(string? appId)
        {
            return appId != null && appRepository.GetAppOrDefault(appId).Id == AppIds.ErrorNotFound;
        }

        void RedirectToAppIfNotError(NavigateArgs navigateArgs, bool replaceHistory = false, string? tabId = null)
        {
            if (IsErrorApp(navigateArgs.AppId)) return;
            client.Redirect(navigateArgs.GetUrl(includeArgs: settings.IncludeArgsInUrl), replaceHistory, tabId);
        }

        void OpenApp(NavigateArgs navigateArgs, bool replaceHistory = false)
        {
            try
            {
                var router = new AppShellRouter();
                var appDescriptor = navigateArgs.AppId != null
                    ? appRepository.GetApp(navigateArgs.AppId)
                    : null;

                var routeResult = router.Route(
                    navigateArgs,
                    settings.Navigation,
                    settings.DefaultAppId,
                    tabs.Value,
                    appDescriptor,
                    settings.PreventTabDuplicates);

                switch (routeResult.Action)
                {
                    case AppShellRouter.RouteAction.OpenPage:
                        HandleOpenPage(navigateArgs, routeResult.EffectiveAppId, replaceHistory);
                        break;

                    case AppShellRouter.RouteAction.SwitchToExistingTab:
                        HandleSwitchToExistingTab(navigateArgs, routeResult.TabIndex!.Value,
                            routeResult.TabId!, replaceHistory);
                        break;

                    case AppShellRouter.RouteAction.RefreshExistingTab:
                        HandleRefreshExistingTab(navigateArgs, routeResult.TabIndex!.Value,
                            routeResult.TabId!, replaceHistory);
                        break;

                    case AppShellRouter.RouteAction.CreateNewTab:
                        HandleCreateNewTab(navigateArgs, routeResult.EffectiveAppId!, replaceHistory);
                        break;

                    case AppShellRouter.RouteAction.Error:
                        client.Error(new InvalidOperationException(routeResult.ErrorMessage));
                        break;

                    case AppShellRouter.RouteAction.Noop:
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TendrilAppShell.OpenApp failed for {AppId}", navigateArgs.AppId);
            }
        }

        void HandleOpenPage(NavigateArgs navigateArgs, string? effectiveAppId, bool replaceHistory)
        {
            var previousApp = currentApp.Value?.AppId;
            var effectiveNavigateArgs = navigateArgs with { AppId = effectiveAppId };

            var appHost = effectiveAppId != null
                ? effectiveNavigateArgs.ToAppHost(args.ConnectionId)
                : null;

            currentApp.Set(appHost);

            if (effectiveAppId != null) SetAppTitle(effectiveAppId);

            if (navigateArgs.HistoryOp is HistoryOp.Push && previousApp != effectiveAppId)
                RedirectToAppIfNotError(effectiveNavigateArgs, replaceHistory);
        }

        void HandleSwitchToExistingTab(NavigateArgs navigateArgs, int tabIndex,
            string tabId, bool replaceHistory)
        {
            var previousSelectedIndex = selectedIndex.Value;
            selectedIndex.Set(tabIndex);
            var tab = tabs.Value[tabIndex];
            SetAppTitle(tab.AppId);

            if (navigateArgs.HistoryOp is HistoryOp.Push && previousSelectedIndex != tabIndex)
                RedirectToAppIfNotError(navigateArgs, replaceHistory, tabId);
        }

        void SetAppTitleAndRedirect(NavigateArgs navigateArgs, string appId, string tabId,
            bool replaceHistory)
        {
            SetAppTitle(appId);
            if (navigateArgs.HistoryOp is HistoryOp.Push)
                RedirectToAppIfNotError(navigateArgs, replaceHistory, tabId);
        }

        void HandleRefreshExistingTab(NavigateArgs navigateArgs, int tabIndex,
            string tabId, bool replaceHistory)
        {
            var tab = tabs.Value[tabIndex];
            tabs.Set(tabs.Value.SetItem(tabIndex, tab with
            {
                AppHost = navigateArgs.ToAppHost(args.ConnectionId),
                RefreshToken = Guid.NewGuid().ToString()
            }));
            selectedIndex.Set(tabIndex);
            SetAppTitleAndRedirect(navigateArgs, tab.AppId, tabId, replaceHistory);
        }

        void HandleCreateNewTab(NavigateArgs navigateArgs, string effectiveAppId,
            bool replaceHistory)
        {
            if (navigateArgs.HistoryOp is not HistoryOp.Push) return;

            var tabId = Guid.NewGuid().ToString();
            var appHost = navigateArgs.ToAppHost(args.ConnectionId);
            var app = appRepository.GetAppOrDefault(effectiveAppId);
            var (tabTitle, tabIcon) = BrandedAppDisplay(app);
            tabTitle = ResolveArgsTabTitle(navigateArgs.AppArgs) ?? tabTitle;

            var newTabs = tabs.Value.Add(new TabState(tabId, app.Id, tabTitle, appHost,
                tabIcon, Guid.NewGuid().ToString()));
            tabs.Set(newTabs);
            selectedIndex.Set(newTabs.Length - 1);
            SetAppTitleAndRedirect(navigateArgs, app.Id, tabId, replaceHistory);
        }

        bool CheckTabExists(int tabId)
        {
            return tabId >= 0 && tabId < tabs.Value.Length;
        }

        void OnMenuSelect(Event<SidebarMenu, object> @event)
        {
            if (@event.Value is string appId) OpenApp(new NavigateArgs(appId));
        }

        ValueTask OnCtrlRightClickSelect(Event<SidebarMenu, object> @event)
        {
            if (@event.Value is string appId) client.OpenUrl(new NavigateArgs(appId, AppShell: false).GetUrl());
            return ValueTask.CompletedTask;
        }

        void OnTabSelect(int tabIndex)
        {
            if (!CheckTabExists(tabIndex)) return;

            if (selectedIndex.Value != tabIndex)
            {
                selectedIndex.Set(tabIndex);
                var tab = tabs.Value[tabIndex];
                SetAppTitle(tab.AppId);
                RedirectToAppIfNotError(new NavigateArgs(tab.AppId), tabId: tab.Id);
            }
        }

        void OnTabClose(int closedIndex)
        {
            if (!CheckTabExists(closedIndex)) return;

            var wasSelected = selectedIndex.Value == closedIndex;
            var newTabs = tabs.Value.RemoveAt(closedIndex);
            int? newIndex = null;
            if (newTabs.Length > 0)
            {
                if (wasSelected)
                    newIndex = Math.Min(closedIndex, newTabs.Length - 1);
                else if (selectedIndex.Value > closedIndex)
                    newIndex = selectedIndex.Value - 1;
                else
                    newIndex = selectedIndex.Value;
            }

            selectedIndex.Set(newIndex);

            if (wasSelected)
            {
                if (newIndex != null)
                {
                    var tab = newTabs[newIndex.Value];
                    SetAppTitle(tab.AppId);
                    RedirectToAppIfNotError(new NavigateArgs(tab.AppId), tabId: tab.Id);
                }
                else
                {
                    client.SetTitle(serverArgs.Metadata.Title);
                    client.Redirect("/");
                    sidebarOpen.Set(true);
                }
            }

            tabs.Set(newTabs);
        }

        void OnTabRefresh(int tabIndex)
        {
            if (!CheckTabExists(tabIndex)) return;

            var tab = tabs.Value[tabIndex];
            tabs.Set(tabs.Value.SetItem(tabIndex, tab with { RefreshToken = Guid.NewGuid().ToString() }));
            selectedIndex.Set(tabIndex);
        }

        void OnTabReorder(int[] newOrder)
        {
            var reorderedTabs = newOrder.Select(index => tabs.Value[index]).ToArray();
            tabs.Set([.. reorderedTabs]);

            if (selectedIndex.Value.HasValue)
            {
                var oldSelectedIndex = selectedIndex.Value.Value;
                var newSelectedIndex = Array.IndexOf(newOrder, oldSelectedIndex);
                if (newSelectedIndex >= 0) selectedIndex.Set(newSelectedIndex);
            }
        }

        object? body;

        if (settings.Navigation == AppShellNavigation.Pages)
        {
            body = currentApp.Value;
        }
        else
        {
            if (tabs.Value.Length == 0)
            {
                body = null;
                if (settings.WallpaperAppId != null)
                    body = new AppHost(settings.WallpaperAppId, null, args.ConnectionId);
            }
            else
            {
                body = Layout.Tabs(tabs.Value.ToArray().Select(e => e.ToTab()).ToArray())
                    .OnSelect(OnTabSelect)
                    .OnClose(OnTabClose)
                    .OnRefresh(OnTabRefresh)
                    .OnReorder(OnTabReorder)
                    .SelectedIndex(selectedIndex.Value)
                    .RemoveParentPadding()
                    .Variant(TabsVariant.Tabs)
                    .Padding(0);
            }
        }

        // Cmd+W (Ctrl+W on Windows) closes the active tab, matching desktop-app convention. ShortcutKey is
        // the only shortcut API Ivy exposes, so the binding lives on a zero-width Ghost button that paints
        // nothing, wrapped in a zero-height stack so it stays out of the layout (same trick as the
        // SelectInput warm-up below).
        object? closeTabShortcut = null;
        if (ShouldEnableCloseTabShortcut(isDesktop, settings.Navigation, tabs.Value.Length, selectedIndex.Value)
            && selectedIndex.Value is { } activeTabIndex)
        {
            closeTabShortcut = Layout.Vertical().Height(Size.Px(0)).Width(Size.Px(0))
                | new Button()
                    .Ghost()
                    .Width(Size.Px(0))
                    .ShortcutKey("Ctrl+W")
                    .OnClick(() => OnTabClose(activeTabIndex));
        }

        var sidebarMenu = new SidebarMenu(
            OnMenuSelect,
            menuItems.Value
        )
        {
            OnCtrlRightClickSelect = new EventHandler<Event<SidebarMenu, object>>(OnCtrlRightClickSelect)
        };

        var settingsMenuItems = new[]
        {
            MenuItem.Default("Configuration")
                .Tag("$setup")
                .Icon(Icons.Construction)
                .OnSelect(() => navigator.Navigate<SettingsApp>()),
            MenuItem.Default("Trash")
                .Tag("$trash")
                .Icon(Icons.Trash2)
                .OnSelect(() => navigator.Navigate<TrashApp>()),
            MenuItem.Default("Import Issues from GitHub")
                .Tag("$import-issues")
                .Icon(Icons.Download)
                .OnSelect(showImportIssuesDialog),
            MenuItem.Default("Check for Updates")
                .Tag("$check-updates")
                .Icon(Icons.CircleArrowUp)
                .OnSelect(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var info = await versionService.CheckForUpdatesAsync(forceRefresh: true);
                            if (info.HasUpdate)
                            {
                                showUpdateDialog(info);
                            }
                            else if (info.LatestVersion == null)
                            {
                                client.Toast("Couldn't check for updates. Please try again later.", "Update check failed")
                                    .Destructive();
                            }
                            else
                            {
                                client.Toast($"You're on the latest version (v{info.CurrentVersion}).", "Up to date")
                                    .Success();
                            }
                        }
                        catch (Exception ex)
                        {
                            client.Toast($"Couldn't check for updates: {ex.Message}", "Update check failed")
                                .Destructive();
                        }
                    });
                }),
            MenuItem.Default("Theme")
                .Tag("$theme")
                .Icon(Icons.SunMoon)
                .Children(
                    MenuItem.Checkbox("Light").Icon(Icons.Sun).OnSelect(() => client.SetThemeMode(ThemeMode.Light)),
                    MenuItem.Checkbox("Dark").Icon(Icons.Moon).OnSelect(() => client.SetThemeMode(ThemeMode.Dark)),
                    MenuItem.Checkbox("System").Icon(Icons.SunMoon)
                        .OnSelect(() => client.SetThemeMode(ThemeMode.System))
                ),
#if DEBUG
            MenuItem.Default("Debug")
                .Tag("$debug")
                .Icon(Icons.Bug)
                .Children(
                    MenuItem.Default("Onboarding")
                        .Icon(Icons.Rocket)
                        .OnSelect(() => navigator.Navigate<OnboardingApp>())
                ),
#endif
        };

        var settingsTrigger = new Button("Settings")
            .Content(
                Layout.Horizontal().AlignContent(Align.Left)
                | Icons.Settings.ToIcon()
                | Text.P("Settings").Small().Muted()
            )
            .Variant(ButtonVariant.Ghost).Width(Size.Full());

        var settingsTriggerCollapsed = new Button()
            .Icon(Icons.Settings)
            .Tooltip("Settings")
            .Variant(ButtonVariant.Ghost).Width(Size.Full());

        var settingsMenu = new DropDownMenu(
                DropDownMenu.DefaultSelectHandler(),
                settingsTrigger)
            .Top()
            .Items(settings.FooterMenuItemsTransformer(settingsMenuItems, navigator));

        var settingsMenuCollapsed = new DropDownMenu(
                DropDownMenu.DefaultSelectHandler(),
                settingsTriggerCollapsed)
            .Width(Size.Full())
            .Top()
            .Items(settings.FooterMenuItemsTransformer(settingsMenuItems, navigator));

        object? footer = settingsMenu;

        if (config.ParseError != null)
            return new ConfigErrorApp(config);

        if (config.NeedsOnboarding) return new OnboardingApp();

        // Warm up SelectInput so its frontend chunk is loaded before dialogs open.
        var selectInputWarmup = new FuncView(context =>
        {
            var noop = context.UseState<string?>(() => null);
            return Layout.Vertical().Height(Size.Px(0)).Width(Size.Px(0))
                | noop.ToSelectInput(new[] { "_" }.ToOptions()).Disabled();
        });

        return new Fragment(
            selectInputWarmup,
            new SidebarLayout(
                body ?? null!,
                sidebarMenu,
                sidebarHeader: Layout.Vertical()
                    | settings.Header
                    | new NewPlanButton(collapsed: false),
                sidebarFooter: Layout.Vertical(
                    new SidebarNews(newsArticles.Value),
                    settings.Footer,
                    footer
                ),
                width: settings.Width,
                sidebarHeaderCollapsed: Layout.Vertical()
                    | new NewPlanButton(collapsed: true),
                sidebarFooterCollapsed: Layout.Vertical().Width(Size.Full())
                    | settingsMenuCollapsed
            ).Open(sidebarOpen.Value).MainAppSidebar(),
            importIssuesDialog,
            updateDialog,
            closeTabShortcut
        );
    }

    internal record TabState(string Id, string AppId, string Title, AppHost AppHost, Icons? Icon, string RefreshToken)
    {
        public Tab ToTab()
        {
            return new Tab(Title, AppHost).Icon(Icon).Key(StringHelper.GetShortHash(Id + RefreshToken));
        }
    }
}
