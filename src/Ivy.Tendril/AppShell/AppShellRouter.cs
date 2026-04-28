using System.Collections.Immutable;
using Ivy.Core.Apps;

namespace Ivy.Tendril.AppShell;

internal class AppShellRouter
{
    internal record RouteResult
    {
        public required RouteAction Action { get; init; }
        public string? TabId { get; init; }
        public int? TabIndex { get; init; }
        public string? EffectiveAppId { get; init; }
        public string? ErrorMessage { get; init; }
    }

    internal enum RouteAction
    {
        OpenPage,
        SwitchToExistingTab,
        CreateNewTab,
        Error,
        Noop
    }

    internal RouteResult Route(
        NavigateArgs navigateArgs,
        AppShellNavigation navigationMode,
        string? defaultAppId,
        ImmutableArray<TendrilAppShell.TabState> currentTabs,
        AppDescriptor? appDescriptor,
        bool preventTabDuplicates)
    {
        return navigationMode == AppShellNavigation.Pages
            ? RouteForPages(navigateArgs, defaultAppId)
            : RouteForTabs(navigateArgs, currentTabs, appDescriptor, preventTabDuplicates);
    }

    private RouteResult RouteForPages(NavigateArgs navigateArgs, string? defaultAppId)
    {
        var effectiveAppId = navigateArgs.AppId ?? defaultAppId;
        return new RouteResult
        {
            Action = RouteAction.OpenPage,
            EffectiveAppId = effectiveAppId
        };
    }

    private RouteResult RouteForTabs(
        NavigateArgs navigateArgs,
        ImmutableArray<TendrilAppShell.TabState> currentTabs,
        AppDescriptor? appDescriptor,
        bool preventTabDuplicates)
    {
        // Handle TabId lookup
        if (!string.IsNullOrEmpty(navigateArgs.TabId))
        {
            var tabIndex = FindTabIndex(currentTabs, navigateArgs.TabId);
            if (tabIndex >= 0)
            {
                return new RouteResult
                {
                    Action = RouteAction.SwitchToExistingTab,
                    TabIndex = tabIndex,
                    TabId = navigateArgs.TabId
                };
            }

            if (navigateArgs.HistoryOp is HistoryOp.Pop)
            {
                return new RouteResult
                {
                    Action = RouteAction.Error,
                    ErrorMessage = "Tab no longer exists."
                };
            }
        }

        if (navigateArgs.AppId == null)
        {
            return new RouteResult { Action = RouteAction.Noop };
        }

        // Check for duplicate tabs
        if (preventTabDuplicates)
        {
            var existingTabIndex = FindTabIndexByAppId(currentTabs, navigateArgs.AppId);
            if (existingTabIndex >= 0 && appDescriptor?.AllowDuplicateTabs != true)
            {
                return new RouteResult
                {
                    Action = RouteAction.SwitchToExistingTab,
                    TabIndex = existingTabIndex,
                    TabId = currentTabs[existingTabIndex].Id
                };
            }
        }

        // Create new tab
        return new RouteResult
        {
            Action = RouteAction.CreateNewTab,
            EffectiveAppId = navigateArgs.AppId
        };
    }

    private static int FindTabIndex(ImmutableArray<TendrilAppShell.TabState> tabs, string tabId)
    {
        for (var i = 0; i < tabs.Length; i++)
            if (tabs[i].Id == tabId) return i;
        return -1;
    }

    private static int FindTabIndexByAppId(ImmutableArray<TendrilAppShell.TabState> tabs, string appId)
    {
        for (var i = 0; i < tabs.Length; i++)
            if (tabs[i].AppId == appId) return i;
        return -1;
    }
}
