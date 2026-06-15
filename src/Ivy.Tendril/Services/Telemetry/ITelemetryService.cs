using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Telemetry;

public interface ITelemetryService
{
    string AnonymousId { get; }
    void TrackAppStarted(AppStartContext context);
    void TrackPlanCreated(PlanCreatedContext context);
    void TrackPrCreated(PrCreatedContext context);
    void TrackJobCompleted(string jobType, JobStatus status, int? durationSeconds, string? agent = null);
    void TrackPlanStateTransition(string fromState, string toState);
    Task IdentifyAsync(string appVersion);
    Task FlushAsync();
}

public record AppStartContext(
    string Version,
    int ProjectCount,
    bool LlmConfigured);

public record PlanCreatedContext(
    string Level,
    int? DurationSeconds,
    string? Agent = null);

public record PrCreatedContext(
    int? DurationSeconds,
    string? Agent = null);
