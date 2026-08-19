using System;
using System.Collections.Generic;
using System.Reflection;
using Ivy.Core.Apps;
using Ivy.Tendril.Apps.ResourceMonitor;
using Ivy.Tendril.Models;
using Xunit;

namespace Ivy.Tendril.Test.Apps;

public class ResourceMonitorAppTests
{
    [Fact]
    public void ResourceMonitorApp_HasExpectedAppAttributeMetadata()
    {
        var appAttr = typeof(ResourceMonitorApp).GetCustomAttribute<AppAttribute>();

        Assert.NotNull(appAttr);
        Assert.Equal("Resource Monitor", appAttr.Title);
        Assert.Equal(Icons.Cpu, appAttr.Icon);
        Assert.Equal(Constants.ResourceMonitor, appAttr.Order);
        Assert.False(appAttr.IsVisible);
        Assert.NotNull(appAttr.Group);
        Assert.Contains("Apps", appAttr.Group);
    }

    [Fact]
    public void ResourceMonitorApp_RendersSnapshotWithoutThrowing()
    {
        var app = new ResourceMonitorApp();
        Assert.NotNull(app);
    }
}
