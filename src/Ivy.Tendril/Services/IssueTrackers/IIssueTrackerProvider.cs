using Ivy;
using Ivy.Tendril.Services.IssueTrackers.Models;

namespace Ivy.Tendril.Services.IssueTrackers;

public record ProviderResult<T>(T Value, string? Error = null)
{
    public bool IsSuccess => Error == null;
    public static ProviderResult<T> Success(T value) => new(value);
    public static ProviderResult<T> Failure(string error, T defaultValue) => new(defaultValue, error);
}

public interface IIssueTrackerProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Icons Icon { get; }

    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetMyAssignedIssuesAsync(CancellationToken ct = default);

    Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetMyAssignedIssuesAsync(
        TrackerConnectionConfig? connection,
        CancellationToken ct = default) => GetMyAssignedIssuesAsync(ct);

    Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesAsync(
        ProjectConfig project,
        TrackerIssueQuery query,
        CancellationToken ct = default);

    Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesForTrackerAsync(
        ProjectConfig project,
        ProjectTrackerConfig tracker,
        TrackerIssueQuery query,
        CancellationToken ct = default) => GetProjectIssuesAsync(project, query, ct);
}

public interface IReviewTrackerProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Icons Icon { get; }

    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<ProviderResult<IReadOnlyList<TrackerReviewItem>>> GetReviewRequestsAsync(CancellationToken ct = default);
}
