using System.Reflection;
using Ivy.Core.Apps;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Chat;
using Xunit;

namespace Ivy.Tendril.Test;

public class BetaAppGatingTests
{
    [Fact]
    public void NonBetaMode_FiltersOutBetaApps()
    {
        var assembly = typeof(TendrilServer).Assembly;
        var apps = AppHelpers.GetApps(assembly)
            .Where(app => app.Type?.GetCustomAttribute<BetaAppAttribute>() == null)
            .ToArray();

        Assert.Contains(apps, a => a.Type == typeof(AgentApp));
        Assert.DoesNotContain(apps, a => a.Type == typeof(ChatApp));
    }

    [Fact]
    public void BetaMode_IncludesBetaApps()
    {
        var assembly = typeof(TendrilServer).Assembly;
        var apps = AppHelpers.GetApps(assembly)
            .ToArray();

        Assert.Contains(apps, a => a.Type == typeof(AgentApp));
        Assert.Contains(apps, a => a.Type == typeof(ChatApp));
    }
}
