using System.Reflection;
using Ivy.Tendril.Apps;

namespace Ivy.Tendril.Test.Apps;

public class AboutAppTests
{
    [Fact]
    public void AboutApp_HasAppAttribute_WithExpectedProperties()
    {
        var appAttr = typeof(AboutApp).GetCustomAttribute<AppAttribute>();
        Assert.NotNull(appAttr);
        Assert.Equal("About", appAttr.Title);
        Assert.Equal(Icons.Info, appAttr.Icon);
        Assert.False(appAttr.IsVisible);
    }
}
