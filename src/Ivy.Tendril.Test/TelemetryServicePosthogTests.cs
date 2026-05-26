using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Test;

public class TelemetryServicePosthogTests
{
    [Fact(Skip = "Manual — run explicitly to verify PostHog ingestion")]
    public async Task SendTestEvent_IngestsSuccessfully()
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<TelemetryService>();

        await using var sut = new TelemetryService(enabled: true, logger: logger);

        Assert.False(string.IsNullOrEmpty(sut.AnonymousId), "AnonymousId should be set when telemetry is enabled");

        sut.TrackAppStarted(new AppStartContext(
            Version: "0.0.0-e2e-test",
            ProjectCount: 0,
            LlmConfigured: false));

        sut.TrackJobCompleted(
            jobType: "e2e_posthog_test",
            status: JobStatus.Completed,
            durationSeconds: 1,
            agent: "test-harness");

        await sut.FlushAsync();
    }
}
