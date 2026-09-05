namespace Ivy.Tendril.Services.IssueTrackers.Models;

public record TrackerIssue(
    string Id,
    string Key,
    string Title,
    string? Body,
    string[] Labels,
    string[] Assignees,
    string? Scope,
    string? Url,
    string ProviderId,
    string Status = "Open",
    string? Priority = null,
    DateTimeOffset? UpdatedAt = null
);

public record TrackerReviewItem(
    string Id,
    string Key,
    string Title,
    string? Body,
    string[] Labels,
    string[] Assignees,
    string Repository,
    string Url,
    string? Branch,
    string ProviderId,
    DateTimeOffset? UpdatedAt = null
);

public record TrackerIssueQuery(
    string? SearchText = null,
    string[]? Labels = null,
    string[]? Assignees = null,
    int Limit = 100
);
