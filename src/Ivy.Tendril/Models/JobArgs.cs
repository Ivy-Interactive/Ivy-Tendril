using System.Text.Json.Serialization;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Models;

[JsonDerivedType(typeof(CreatePlanArgs), "CreatePlan")]
[JsonDerivedType(typeof(ExecutePlanArgs), "ExecutePlan")]
[JsonDerivedType(typeof(RetryPlanArgs), "RetryPlan")]
[JsonDerivedType(typeof(ExpandPlanArgs), "ExpandPlan")]
[JsonDerivedType(typeof(UpdatePlanArgs), "UpdatePlan")]
[JsonDerivedType(typeof(SplitPlanArgs), "SplitPlan")]
[JsonDerivedType(typeof(CreatePrArgs), "CreatePr")]
[JsonDerivedType(typeof(CreateIssueArgs), "CreateIssue")]
[JsonDerivedType(typeof(SetupProjectArgs), "SetupProject")]
[JsonDerivedType(typeof(SyncRepoArgs), "SyncRepo")]
[JsonDerivedType(typeof(AddProjectArgs), "AddProject")]
[JsonDerivedType(typeof(UpdateMemoriesArgs), "UpdateMemories")]
[JsonDerivedType(typeof(PurgeMemoriesArgs), "PurgeMemories")]
[JsonDerivedType(typeof(CompactMemoriesArgs), "CompactMemories")]
public abstract record JobArgsBase
{
    [JsonIgnore]
    public abstract string Type { get; }
    [JsonIgnore]
    public virtual string? PlanFolder => null;
    public List<string>? WaitForJobs { get; init; }
}

public record CreatePlanArgs(
    string Description,
    string Project,
    int Priority = 0,
    bool Force = false,
    string? SourcePath = null,
    string? UploadSessionId = null) : JobArgsBase
{
    public override string Type => Constants.JobTypes.CreatePlan;
}

public record ExecutePlanArgs(
    string FolderPath,
    string? Note = null) : JobArgsBase
{
    public override string Type => Constants.JobTypes.ExecutePlan;
    public override string PlanFolder => FolderPath;
}

public record RetryPlanArgs(
    string FolderPath,
    string ChangeRequest) : JobArgsBase
{
    public override string Type => Constants.JobTypes.RetryPlan;
    public override string PlanFolder => FolderPath;
}

public record ExpandPlanArgs(
    string FolderPath) : JobArgsBase
{
    public override string Type => Constants.JobTypes.ExpandPlan;
    public override string PlanFolder => FolderPath;
}

public record UpdatePlanArgs(
    string FolderPath,
    string? Instructions = null,
    string? UploadSessionId = null) : JobArgsBase
{
    public override string Type => Constants.JobTypes.UpdatePlan;
    public override string PlanFolder => FolderPath;
}

public record SplitPlanArgs(
    string FolderPath) : JobArgsBase
{
    public override string Type => Constants.JobTypes.SplitPlan;
    public override string PlanFolder => FolderPath;
}

public record CreatePrArgs(
    string FolderPath,
    bool SolveMergeConflicts = true,
    bool Merge = true,
    bool DeleteBranch = true,
    bool IncludeArtifacts = true,
    string[]? Reviewers = null,
    string? Comment = null,
    bool Draft = false) : JobArgsBase
{
    public override string Type => Constants.JobTypes.CreatePr;
    public override string PlanFolder => FolderPath;
}

public record CreateIssueArgs(
    string FolderPath,
    string Repo,
    string? Assignee = null,
    string? Comment = null,
    string? Labels = null) : JobArgsBase
{
    public override string Type => Constants.JobTypes.CreateIssue;
    public override string PlanFolder => FolderPath;
}

public record SetupProjectArgs(
    string FolderPath) : JobArgsBase
{
    public override string Type => Constants.JobTypes.SetupProject;
    public override string PlanFolder => FolderPath;
}

public record SyncRepoArgs(
    string RepoPath,
    string BaseBranch,
    string? PlanFolderPath = null,
    UntrackedChangesPolicy UntrackedChangesPolicy = UntrackedChangesPolicy.Stash) : JobArgsBase
{
    public override string Type => Constants.JobTypes.SyncRepo;
    public override string? PlanFolder => PlanFolderPath;
}

public record AddProjectArgs(
    string ProjectName,
    List<RepoRef> Repos) : JobArgsBase
{
    public override string Type => Constants.JobTypes.AddProject;
}

public record UpdateMemoriesArgs(
    string TendrilProject,
    string FilesToUpdate) : JobArgsBase
{
    public override string Type => "UpdateMemories";
}

public record PurgeMemoriesArgs(
    string TendrilProject) : JobArgsBase
{
    public override string Type => "PurgeMemories";
}

public record CompactMemoriesArgs(
    string TendrilProject) : JobArgsBase
{
    public override string Type => "CompactMemories";
}

// How SyncRepo should treat uncommitted changes and/or untracked files when syncing a repo.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UntrackedChangesPolicy
{
    // Preserve local work in a named stash (default; never loses work).
    Stash,
    // Group changes into logical commits and push them to the base branch.
    Commit,
    // Group changes into logical commits on a new branch and open a pull request.
    PullRequest
}
