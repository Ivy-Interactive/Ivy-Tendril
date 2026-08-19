using System;
using System.Linq;
using Ivy.Core.Apps;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Apps.ResourceMonitor;
using Xunit;

namespace Ivy.Tendril.Test;

public class TendrilServerBetaAppTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void BetaApps_VisibilityFollowsIsBetaFlag(bool isBeta, bool expectedVisibility)
    {
        var assembly = typeof(TendrilServer).Assembly;
        var apps = AppHelpers.GetApps(assembly)
            .Select(app => (app.Type == typeof(ChatApp) || app.Type == typeof(ResourceMonitorApp)) ? new AppDescriptor
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
            .ToArray();

        var resourceMonitor = apps.FirstOrDefault(a => a.Type == typeof(ResourceMonitorApp));
        Assert.NotNull(resourceMonitor);
        Assert.Equal(expectedVisibility, resourceMonitor.IsVisible);

        var chatApp = apps.FirstOrDefault(a => a.Type == typeof(ChatApp));
        Assert.NotNull(chatApp);
        Assert.Equal(expectedVisibility, chatApp.IsVisible);
    }
}
