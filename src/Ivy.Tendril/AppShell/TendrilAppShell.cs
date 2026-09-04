using System.Collections.Immutable;
using System.Reactive.Disposables;
using Ivy.Core;
using Ivy.Core.Apps;
using Ivy.Desktop;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.AppShell.Dialogs;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Icebox;
using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.PullRequest;
using Ivy.Tendril.Apps.ReviewAction;
using Ivy.Tendril.Apps.Settings;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Themes;
using Ivy.Tendril.Widgets;
using Ivy.Widgets.Internal;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.AppShell;

#pragma warning disable IVYAPP001

public class TendrilAppShell(AppShellSettings settings) : ViewBase
{
    internal AppShellSettings Settings => settings;

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
        ReviewActionAppArgs { ProjectName: { Length: > 0 } projectName, ActionName: { Length: > 0 } name }
            => $"[{projectName}] {name}",
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
    ///     Whether Cmd+W / Ctrl+W should be wired up to close the active session tab. Desktop shell
    ///     only: in a browser the chord belongs to the browser (it closes the browser tab and cannot
    ///     be cancelled), and <see cref="AppShellNavigation.Pages"/> has no tab strip at all.
    /// </summary>
    internal static bool ShouldEnableCloseTabShortcut(
        bool isDesktop, AppShellNavigation navigation, int tabCount, int? selectedIndex)
        => isDesktop
           && navigation != AppShellNavigation.Pages
           && selectedIndex is { } index
           && index >= 0
           && index < tabCount;

    internal static MenuItem[] BuildHelpMenuItems(bool isBeta, IClientProvider? client, INavigator? navigator)
    {
        var items = new List<MenuItem>
        {
            MenuItem.Default("Documentation").Icon(Icons.ExternalLink).OnSelect(() => client?.OpenUrl(Constants.DocsUrl)),
            MenuItem.Default("Discord").Icon(Icons.Discord).OnSelect(() => client?.OpenUrl(Constants.DiscordUrl)),
            MenuItem.Default("Report Issue").Icon(Icons.Bug).OnSelect(() => client?.OpenUrl(Constants.IssuesUrl))
        };

        if (isBeta)
        {
            items.Add(MenuItem.Default("About").Icon(Icons.Info).OnSelect(() => navigator?.Navigate<AboutApp>()));
        }

        return items.ToArray();
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

    private static readonly HashSet<string> SidebarSectionAppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "review", "plans", "drafts", "recommendations"
    };

    internal static bool HasSidebarSection(string? appId) =>
        appId != null && SidebarSectionAppIds.Contains(appId);

    private static readonly HashSet<string> ShareAllowedAppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "review", "plans"
    };

    private static MenuItem? FilterMenuItemForShare(MenuItem item, HashSet<string> allowedTags)
    {
        if (item.Children is { Length: > 0 } children)
        {
            var filteredChildren = children
                .Select(c => FilterMenuItemForShare(c, allowedTags))
                .Where(c => c != null)
                .Select(c => c!)
                .ToArray();

            if (filteredChildren.Length == 0)
                return null;

            return item with { Children = filteredChildren };
        }

        if (item.Tag is string tag && allowedTags.Contains(tag))
        {
            return item;
        }

        return null;
    }

    private static string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "R";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    private static MenuItem[] BuildMenuItems(IAppRepository repo, TendrilProcessStatus status,
        IAgentRunner runner, bool isShareMode = false)
    {
        var nonChatAgentCount = Math.Max(0, runner.ActiveSessions.Count - status.GeneratingChatSessionsCount);
        var badges = new Dictionary<string, int>
        {
            ["plans"] = status.DraftCount,
            ["review"] = status.ReviewCount,
            ["jobs"] = status.JobCount,
            ["icebox"] = status.IceboxCount,
            ["recommendations"] = status.RecommendationsCount,
            ["chat"] = status.GeneratingChatSessionsCount,
            ["agent"] = nonChatAgentCount
        };
        var items = repo.GetMenuItems()
            .Select(m => AddBadge(m, badges));

        if (isShareMode)
        {
            items = items
                .Select(m => FilterMenuItemForShare(m, ShareAllowedAppIds))
                .Where(m => m != null)
                .Select(m => m!);
        }

        return items.ToArray();
    }

    /// <summary>
    ///     Flattens the app menu into the sidebar nav rows. The agent entry is excluded — it is
    ///     rendered as the dedicated agent button above the nav instead.
    /// </summary>
    internal static List<ShellNavItemDto> BuildNavItems(MenuItem[] menuItems, string? activeAppId)
    {
        var result = new List<ShellNavItemDto>();

        void AddLeaf(MenuItem item)
        {
            if (item.Tag is not string tag || tag == AgentAppId) return;
            result.Add(new ShellNavItemDto(
                tag,
                item.Label ?? tag,
                item.Icon is { } icon && icon != Icons.None ? icon.ToString() : null,
                item.Badge,
                string.Equals(tag, activeAppId, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var item in menuItems)
        {
            if (item.Children is { Length: > 0 })
            {
                foreach (var child in item.Children) AddLeaf(child);
            }
            else
            {
                AddLeaf(item);
            }
        }

        return result;
    }

    public override object Build()
    {
        // All hooks must be at the top level of Build()
        var config = UseService<IConfigService>();
        var shareContext = UseService<Ivy.Tendril.Services.Share.IShareContext>();
        var logger = UseService<ILogger<TendrilAppShell>>();
        var tabs = UseState(ImmutableArray.Create<TabState>);
        var selectedIndex = UseState<int?>();
        var appRepository = UseService<IAppRepository>();
        var client = UseService<IClientProvider>();
        var versionService = UseService<IVersionCheckService>();
        var currentApp = UseState<AppHost?>();
        var statusService = UseService<ITendrilProcessStatusService>();
        var agentRunner = UseService<IAgentRunner>();
        var menuItems = UseState(() => BuildMenuItems(appRepository, statusService.Current, agentRunner, shareContext.IsShareMode));
        var status = UseState(() => statusService.Current);
        var sidebarOpen = UseState(() => config.Settings.SidebarOpen);
        var sidebarList = UseState<ShellSidebarListState?>(() => null);
        var lastSessionIndex = UseRef<int?>(null);
        var args = UseService<AppContext>();
        var serverArgs = UseService<ServerArgs>();
        var navigate = Context.UseSignal<NavigateSignal, NavigateArgs, Unit>();
        var sidebarListSignal = Context.UseSignal<ShellSidebarListSignal, ShellSidebarListState, Unit>();
        var navigator = UseNavigation();
        var jobService = UseService<IJobService>();
        Context.TryUseService<DesktopWindow>(out var desktopWindow);
        Context.TryUseService<TendrilArgs>(out var tendrilArgs);

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

        var (planSearchDialog, showPlanSearchDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new PlanSearchDialog(isOpen);
        });

        var isShareMode = shareContext.IsShareMode;

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

        UseEffect(() =>
        {
            return sidebarListSignal.Receive(state => sidebarList.Set(state));
        });

        UseEffect(() => { menuItems.Set(BuildMenuItems(appRepository, status.Value, agentRunner, shareContext.IsShareMode)); },
            appRepository.Reloaded.ToTrigger(), status);

        // Apply configured theme on mount
        UseEffect(() =>
        {
            TendrilThemes.ApplyTheme(client, config.Settings.Theme);
            TendrilThemes.ApplyThemeMode(client, config.Settings.ThemeMode);
        });

        // Rebuild the menu and reapply theme when settings are saved (e.g. the coding agent or theme changes),
        // so the UI updates immediately without needing a reload.
        UseEffect(() =>
        {
            void OnSettingsReloaded(object? sender, EventArgs e)
            {
                menuItems.Set(BuildMenuItems(appRepository, status.Value, agentRunner, shareContext.IsShareMode));
                sidebarOpen.Set(config.Settings.SidebarOpen);
                TendrilThemes.ApplyTheme(client, config.Settings.Theme);
                TendrilThemes.ApplyThemeMode(client, config.Settings.ThemeMode);
            }
            config.SettingsReloaded += OnSettingsReloaded;
            return Disposable.Create(() => config.SettingsReloaded -= OnSettingsReloaded);
        });

        UseEffect(() =>
        {
            void OnNotification(JobNotification notification)
            {
                // Read the setting at notification time: the user can toggle it in Settings while
                // the shell is mounted, and the effect does not re-subscribe on that change.
                if (!ShouldShowInAppToast(desktopWindow != null, config.Settings.DesktopNotifications))
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
            if (config.NeedsOnboarding && !shareContext.IsShareMode) return;

            var defaultAppId = shareContext.IsShareMode ? "review" : settings.DefaultAppId;
            var initialAppId = args.NavigationAppId ?? defaultAppId;
            var targetAppId = initialAppId;
            if (!string.IsNullOrWhiteSpace(targetAppId))
            {
                // Force redirect from onboarding if it's already done
                if (!config.NeedsOnboarding && OnboardingAppIds.Contains(targetAppId))
                    targetAppId = defaultAppId;

                var appArgs = args.GetArgs<object>();
                OpenApp(new NavigateArgs(targetAppId, appArgs), true);
            }
            else
            {
                client.Redirect(shareContext.IsShareMode ? "/review" : "/", true);
            }
        });

        var isBeta = BetaHelper.IsBeta(tendrilArgs, config);
        var isDesktop = desktopWindow != null;

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
                if (isShareMode && navigateArgs.AppId != null && !ShareAllowedAppIds.Contains(navigateArgs.AppId))
                {
                    navigateArgs = navigateArgs with { AppId = "review" };
                }

                var router = new AppShellRouter();
                var appDescriptor = navigateArgs.AppId != null
                    ? appRepository.GetApp(navigateArgs.AppId)
                    : null;

                var routeResult = router.Route(
                    navigateArgs,
                    settings.Navigation,
                    settings.DefaultAppId,
                    tabs.Value,
                    appDescriptor);

                switch (routeResult.Action)
                {
                    case AppShellRouter.RouteAction.OpenPage:
                        HandleOpenPage(navigateArgs, routeResult.EffectiveAppId, replaceHistory);
                        break;

                    case AppShellRouter.RouteAction.SwitchToExistingTab:
                        HandleSwitchToExistingTab(navigateArgs, routeResult.TabIndex!.Value,
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
            var wasOnSession = selectedIndex.Value != null;
            var effectiveNavigateArgs = navigateArgs with { AppId = effectiveAppId };

            var appHost = effectiveAppId != null
                ? effectiveNavigateArgs.ToAppHost(args.ConnectionId)
                : null;

            currentApp.Set(appHost);
            selectedIndex.Set((int?)null);

            // The sidebar section belongs to the page app; drop it when the page changes to an
            // app without a sidebar list. Retain it when transitioning between apps that both
            // display sidebar sections so the header and search button do not flicker.
            if (sidebarList.Value is { } list &&
                !string.Equals(list.AppId, effectiveAppId, StringComparison.OrdinalIgnoreCase) &&
                !HasSidebarSection(effectiveAppId))
                sidebarList.Set((ShellSidebarListState?)null);

            if (effectiveAppId != null) SetAppTitle(effectiveAppId);

            if (navigateArgs.HistoryOp is HistoryOp.Push && (previousApp != effectiveAppId || wasOnSession))
                RedirectToAppIfNotError(effectiveNavigateArgs, replaceHistory);
        }

        void HandleSwitchToExistingTab(NavigateArgs navigateArgs, int tabIndex,
            string tabId, bool replaceHistory)
        {
            var previousSelectedIndex = selectedIndex.Value;
            selectedIndex.Set(tabIndex);
            lastSessionIndex.Value = tabIndex;
            var tab = tabs.Value[tabIndex];
            SetAppTitle(tab.AppId);

            if (navigateArgs.HistoryOp is HistoryOp.Push && previousSelectedIndex != tabIndex)
                RedirectToAppIfNotError(navigateArgs, replaceHistory, tabId);
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
            lastSessionIndex.Value = newTabs.Length - 1;
            SetAppTitle(app.Id);
            RedirectToAppIfNotError(navigateArgs, replaceHistory, tabId);
        }

        bool CheckTabExists(int tabIndex)
        {
            return tabIndex >= 0 && tabIndex < tabs.Value.Length;
        }

        int FindTabIndexById(string tabId)
        {
            for (var i = 0; i < tabs.Value.Length; i++)
                if (tabs.Value[i].Id == tabId) return i;
            return -1;
        }

        void SelectSession(int tabIndex)
        {
            if (!CheckTabExists(tabIndex)) return;
            if (selectedIndex.Value == tabIndex) return;

            selectedIndex.Set(tabIndex);
            lastSessionIndex.Value = tabIndex;
            var tab = tabs.Value[tabIndex];
            SetAppTitle(tab.AppId);
            RedirectToAppIfNotError(new NavigateArgs(tab.AppId), tabId: tab.Id);
        }

        void OnTabClose(int closedIndex)
        {
            if (!CheckTabExists(closedIndex)) return;

            var wasSelected = selectedIndex.Value == closedIndex;
            var newTabs = tabs.Value.RemoveAt(closedIndex);
            int? newIndex = null;
            if (wasSelected)
            {
                newIndex = newTabs.Length > 0 ? Math.Min(closedIndex, newTabs.Length - 1) : null;
            }
            else if (selectedIndex.Value is { } current)
            {
                newIndex = current > closedIndex ? current - 1 : current;
            }

            selectedIndex.Set(newIndex);

            // The last-visited pointer tracks its own tab, not the selection: renumber it for
            // the removal and only drop it when the tab it pointed at is the one closing.
            if (lastSessionIndex.Value is { } lastTracked)
            {
                if (lastTracked == closedIndex) lastSessionIndex.Value = wasSelected ? newIndex : null;
                else if (lastTracked > closedIndex) lastSessionIndex.Value = lastTracked - 1;
            }

            tabs.Set(newTabs);

            if (!wasSelected) return;

            if (newIndex is { } idx)
            {
                var tab = newTabs[idx];
                SetAppTitle(tab.AppId);
                RedirectToAppIfNotError(new NavigateArgs(tab.AppId), tabId: tab.Id);
            }
            else if (currentApp.Value is { } page)
            {
                // The page is still open behind the sessions; reveal it again.
                SetAppTitle(page.AppId);
                RedirectToAppIfNotError(new NavigateArgs(page.AppId));
            }
            else
            {
                client.SetTitle(serverArgs.Metadata.Title);
                client.Redirect("/");
                sidebarOpen.Set(true);
            }
        }

        var settingsMenuItems = new[]
        {
            MenuItem.Default("Configuration")
                .Tag("$setup")
                .Icon(Icons.Construction)
                .OnSelect(() => navigator.Navigate<SettingsApp>()),
            MenuItem.Default("Pull Requests")
                .Tag("$pull-requests")
                .Icon(Icons.GitPullRequest)
                .OnSelect(() => navigator.Navigate<PullRequestApp>()),
            MenuItem.Default("Icebox")
                .Tag("$icebox")
                .Icon(Icons.Snowflake)
                .OnSelect(() => navigator.Navigate<IceboxApp>()),
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
            MenuItem.Default("Help")
                .Tag("$help")
                .Icon(Icons.CircleQuestionMark)
                .Children(BuildHelpMenuItems(isBeta, client, navigator)),
        };

        if (config.ParseError != null)
            return new ConfigErrorApp(config);

        if (config.NeedsOnboarding && !isShareMode) return new OnboardingApp();

        // ----- Sidebar -----

        var versionString = typeof(TendrilAppShell).Assembly.GetName().Version!.ToString(3);
        var sidebarHeader = new ShellSidebarHeader()
            .Title("Ivy Tendril")
            .Version($"v {versionString}")
            .LogoUrl("/tendril/assets/Tendril.svg");

        // The widget handles Cmd/Ctrl+K client-side; the zero-size ghost button keeps the
        // legacy Ctrl+Alt+N chord working (ShortcutKey is the only shortcut API Ivy exposes).
        var newPlanButton = new CreatePlanDialogLauncher(open => new Fragment(
            new ShellNewPlanButton().OnClick(open),
            Layout.Vertical().Height(Size.Px(0)).Width(Size.Px(0))
            | new Button()
                .Ghost()
                .Width(Size.Px(0))
                .ShortcutKey("CTRL+ALT+N")
                .OnClick(() => open())
        ));

        // While a session pane is visible the agent row is the selected item, so the
        // nav must not keep highlighting the page app behind it.
        var sessionIsActive = selectedIndex.Value is { } activeSession && CheckTabExists(activeSession);
        var activeNavAppId = sessionIsActive ? null : currentApp.Value?.AppId;

        var (agentLabel, _) = AgentBranding.For(config.Settings.CodingAgent, agentRunner, config);
        var agentButton = new ShellAgentButton()
            .IsActive(sessionIsActive)
            .Label(agentLabel)
            .Icon(AgentBranding.IconFor(config.Settings.CodingAgent, config).ToString())
            .OnOpen(() =>
            {
                if (tabs.Value.Length == 0)
                {
                    OpenApp(new NavigateArgs(AgentAppId));
                    return;
                }
                var latest = lastSessionIndex.Value is { } last && CheckTabExists(last)
                    ? last
                    : tabs.Value.Length - 1;
                SelectSession(latest);
            })
            .OnNewChat(() => OpenApp(new NavigateArgs(AgentAppId)));

        object? section = null;
        if (sidebarList.Value is { } list &&
            (string.Equals(list.AppId, currentApp.Value?.AppId, StringComparison.OrdinalIgnoreCase) ||
             HasSidebarSection(currentApp.Value?.AppId)))
        {
            var capturedList = list;
            section = new ShellSidebarSection()
                .Title(list.Title)
                .Items(list.Items)
                .SelectedId(list.SelectedId)
                .Searchable(list.Searchable)
                .OnSelectItem(itemId =>
                    OpenApp(new NavigateArgs(capturedList.AppId, capturedList.BuildSelectArgs(itemId))))
                .OnSearch(showPlanSearchDialog);
        }

        var nav = new ShellNav()
            .Items(BuildNavItems(menuItems.Value, activeNavAppId))
            .ShowDivider(section != null)
            .OnSelect(appId => OpenApp(new NavigateArgs(appId)));

        var settingsMenu = new DropDownMenu(
                DropDownMenu.DefaultSelectHandler(),
                new ShellSettingsButton())
            .Top()
            .Items(settings.FooterMenuItemsTransformer(settingsMenuItems, navigator));

        // ----- Content + session tabs -----

        object? pageContent = currentApp.Value != null
            ? currentApp.Value.Key(currentApp.Value.AppId + (currentApp.Value.AppArgs != null ? ":" + currentApp.Value.AppArgs : ""))
            : null;
        if (pageContent == null && settings.WallpaperAppId != null)
            pageContent = new AppHost(settings.WallpaperAppId, null, args.ConnectionId).Key(settings.WallpaperAppId);

        var sessionContents = tabs.Value
            .Select(t => (object?)t.AppHost.Key(StringHelper.GetShortHash(t.Id + t.RefreshToken)))
            .ToArray();

        var tabsWidget = new ShellTabs()
            .Tabs(tabs.Value.Select(t => new ShellTabDto(t.Id, t.Title)).ToList())
            .SelectedId(selectedIndex.Value is { } si && CheckTabExists(si) ? tabs.Value[si].Id : null)
            .OnSelect(tabId => SelectSession(FindTabIndexById(tabId)))
            .OnClose(tabId => OnTabClose(FindTabIndexById(tabId)))
            .OnNew(() => OpenApp(new NavigateArgs(AgentAppId)));

        // Cmd+W (Ctrl+W on Windows) closes the active session tab, matching desktop-app convention.
        // ShortcutKey is the only shortcut API Ivy exposes, so the binding lives on a zero-width
        // Ghost button that paints nothing, wrapped in a zero-height stack so it stays out of the
        // layout (same trick as the SelectInput warm-up below).
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

        // Warm up SelectInput so its frontend chunk is loaded before dialogs open.
        var selectInputWarmup = new FuncView(context =>
        {
            var noop = context.UseState<string?>(() => null);
            return Layout.Vertical().Height(Size.Px(0)).Width(Size.Px(0))
                | noop.ToSelectInput(new[] { "_" }.ToOptions()).Disabled();
        });

        // Share mode: reviewers get a read-only shell — no plan creation, and the footer
        // identifies the reviewer instead of exposing the settings menu.
        var reviewerPersona = shareContext.Persona;
        var reviewerInitials = GetInitials(reviewerPersona);

        object sidebarFooter = isShareMode
            ? Layout.Horizontal().AlignContent(Align.Left).Height(Size.Auto()).Width(Size.Full())
                | new Avatar(reviewerInitials).Small()
                | Text.Block(reviewerPersona).Small().Bold().Overflow(Overflow.Ellipsis)
            : settingsMenu;

        // A shared link that points straight at one plan renders that plan alone, with no shell
        // chrome around it.
        if (isShareMode && HasDirectPlanId(args))
        {
            var selectedApp = tabs.Value.Length > 0
                ? (selectedIndex.Value is { } shareIndex && CheckTabExists(shareIndex)
                    ? tabs.Value[shareIndex].AppHost
                    : tabs.Value[0].AppHost)
                : pageContent;

            return new Fragment(
                selectInputWarmup,
                selectedApp ?? null!,
                importIssuesDialog,
                updateDialog,
                planSearchDialog,
                closeTabShortcut
            );
        }

        var shell = new TendrilShell(
                sidebarHeader: sidebarHeader,
                sidebarBody: isShareMode
                    ? [nav, section]
                    : [newPlanButton, agentButton, nav, section],
                sidebarFooter: sidebarFooter,
                content: pageContent,
                sessionContents: sessionContents,
                tabs: tabsWidget,
                hidden: [selectInputWarmup, closeTabShortcut])
            .Collapsed(!sidebarOpen.Value)
            .HasTabs(tabs.Value.Length > 0)
            .ActiveSessionIndex(selectedIndex.Value)
            .OnCollapsedChanged(collapsed =>
            {
                sidebarOpen.Set(!collapsed);
                config.Settings.SidebarOpen = !collapsed;
                config.SaveSettings();
            });

        return new Fragment(
            shell,
            importIssuesDialog,
            updateDialog,
            planSearchDialog
        );
    }

    internal static bool HasDirectPlanId(AppContext? appContext)
    {
        if (appContext == null) return false;
        try
        {
            var reviewArgs = appContext.GetArgs<Ivy.Tendril.Apps.Review.ReviewAppArgs>();
            if (!string.IsNullOrEmpty(reviewArgs?.PlanId)) return true;
        }
        catch { }

        try
        {
            var draftsArgs = appContext.GetArgs<Ivy.Tendril.Apps.Plans.PlansAppArgs>();
            if (!string.IsNullOrEmpty(draftsArgs?.PlanId)) return true;
        }
        catch { }

        return false;
    }

    internal record TabState(string Id, string AppId, string Title, AppHost AppHost, Icons? Icon, string RefreshToken);
}
