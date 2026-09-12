using Ivy.Tendril.Services.Telemetry;

namespace Ivy.Tendril.Test.Services;

public class TelemetryServiceTests
{
    [Fact]
    public void CreatePostHogOptions_IncludesDistributionAndSource_MatchingAppBrand()
    {
        var options = TelemetryService.CreatePostHogOptions("test-session-id", "1.0.0");

        Assert.NotNull(options);
        Assert.NotNull(options.SuperProperties);

        Assert.True(options.SuperProperties.ContainsKey("distribution"));
        Assert.Equal(AppBrand.Distribution, options.SuperProperties["distribution"]);

        Assert.True(options.SuperProperties.ContainsKey("source"));
        Assert.Equal(AppBrand.Distribution, options.SuperProperties["source"]);

        Assert.True(options.SuperProperties.ContainsKey("app_version"));
        Assert.Equal("1.0.0", options.SuperProperties["app_version"]);
    }
}
