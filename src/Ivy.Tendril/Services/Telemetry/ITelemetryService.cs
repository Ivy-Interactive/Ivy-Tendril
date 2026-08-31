using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Telemetry;

public interface ITelemetryService
{
    string AnonymousId { get; }
    void TrackAppStarted(AppStartContext context);
    void TrackOnboardingCompleted(OnboardingCompletedContext context);
    void TrackProjectCreated(ProjectCreatedContext context);
    void TrackPlanCreated(PlanCreatedContext context);
    void TrackJobCreated(JobCreatedContext context);
    void TrackPrCreated(PrCreatedContext context);
    void TrackJobCompleted(string jobType, JobStatus status, int? durationSeconds, string? agent = null,
        string? planId = null);
    void TrackPlanStateTransition(string fromState, string toState, string? planId = null);
    Task IdentifyAsync(string appVersion);
    Task FlushAsync();
}

public record AppStartContext(
    string Version,
    int ProjectCount,
    bool LlmConfigured);

public record OnboardingCompletedContext(
    int ProjectCount,
    string? Agent = null);

/// <param name="StackHash">
///     Usually null: SetupProject assigns the stack hash after the project exists, so it is only
///     populated here when a project is created from an already-analyzed configuration.
/// </param>
public record ProjectCreatedContext(
    int RepoCount,
    string? StackHash = null);

public record JobCreatedContext(
    string JobType,
    string? Agent = null,
    string? PlanId = null);

public record PlanCreatedContext(
    string Level,
    int? DurationSeconds,
    string? Agent = null,
    string? StackHash = null,
    string? PlanId = null);

public record PrCreatedContext(
    int? DurationSeconds,
    string? Agent = null,
    string? PlanId = null);
