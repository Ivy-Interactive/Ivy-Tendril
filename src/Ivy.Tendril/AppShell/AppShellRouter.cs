using System.Collections.Immutable;
using Ivy.Core.Apps;

namespace Ivy.Tendril.AppShell;

/// <summary>
///     Routing for the hybrid shell: regular apps render as the single page inside the
///     content frame, while session apps (AllowDuplicateTabs — agent terminals and
///     review actions) open as tabs in the bottom session strip. Pages navigation mode
///     keeps its all-pages behavior.
/// </summary>
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
        ImmutableArray<TendrilAppShell.TabState> sessionTabs,
        AppDescriptor? appDescriptor)
    {
        return navigationMode == AppShellNavigation.Pages
            ? RouteForPages(navigateArgs, defaultAppId)
            : RouteHybrid(navigateArgs, sessionTabs, appDescriptor);
    }

    private static RouteResult RouteForPages(NavigateArgs navigateArgs, string? defaultAppId)
    {
        var effectiveAppId = navigateArgs.AppId ?? defaultAppId;
        return new RouteResult
        {
            Action = RouteAction.OpenPage,
            EffectiveAppId = effectiveAppId
        };
    }

    private static RouteResult RouteHybrid(
        NavigateArgs navigateArgs,
        ImmutableArray<TendrilAppShell.TabState> sessionTabs,
        AppDescriptor? appDescriptor)
    {
        // A TabId means restoring an existing session tab (e.g. browser history).
        if (!string.IsNullOrEmpty(navigateArgs.TabId))
        {
            var tabIndex = FindTabIndex(sessionTabs, navigateArgs.TabId);
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

        if (appDescriptor?.AllowDuplicateTabs == true)
        {
            return new RouteResult
            {
                Action = RouteAction.CreateNewTab,
                EffectiveAppId = navigateArgs.AppId
            };
        }

        return new RouteResult
        {
            Action = RouteAction.OpenPage,
            EffectiveAppId = navigateArgs.AppId
        };
    }

    private static int FindTabIndex(ImmutableArray<TendrilAppShell.TabState> tabs, string tabId)
    {
        for (var i = 0; i < tabs.Length; i++)
            if (tabs[i].Id == tabId) return i;
        return -1;
    }
}
