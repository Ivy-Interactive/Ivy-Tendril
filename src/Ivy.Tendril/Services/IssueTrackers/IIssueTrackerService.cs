using Ivy.Tendril.Services.IssueTrackers.Models;

namespace Ivy.Tendril.Services.IssueTrackers;

public interface IIssueTrackerService
{
    IReadOnlyList<IIssueTrackerProvider> IssueProviders { get; }
    IReadOnlyList<IReviewTrackerProvider> ReviewProviders { get; }

    IIssueTrackerProvider? GetIssueProvider(string providerId);
    IReviewTrackerProvider? GetReviewProvider(string providerId);

    Task<(List<TrackerIssue> Issues, List<string> Errors)> GetMyAssignedIssuesAsync(CancellationToken ct = default);

    Task<(List<TrackerReviewItem> Reviews, List<string> Errors)> GetReviewRequestsAsync(CancellationToken ct = default);

    Task<(List<TrackerIssue> Issues, List<string> Errors)> GetProjectIssuesAsync(
        ProjectConfig project,
        TrackerIssueQuery query,
        CancellationToken ct = default);
}
