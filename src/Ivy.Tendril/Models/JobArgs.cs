using System.Text.Json.Serialization;
using Ivy.Tendril.Helpers;
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
public abstract record JobArgsBase
{
    [JsonIgnore]
    public abstract string Type { get; }
    [JsonIgnore]
    public virtual string? PlanFolder => null;

    /// <summary>
    ///     What makes two submissions of this job type the same request. Null means this type is not
    ///     deduplicated: a deliberate, visible opt-out, not an omission. Non-null values are compared
    ///     case-insensitively within the same <see cref="Type" />.
    ///     <para>
    ///     Abstract rather than virtual on purpose. The guard this feeds used to be an allow-list of
    ///     five type names, so every job type added afterwards shipped undeduplicated until someone
    ///     noticed the hard way (#2710): 99 CreatePlan jobs from 39 descriptions in one hour, and five
    ///     CreatePr jobs on one plan in 82 seconds. A new subtype now fails to compile until its author
    ///     states an answer.
    ///     </para>
    /// </summary>
    [JsonIgnore]
    public abstract string? ConflictKey { get; }

    /// <summary>
    ///     Which set of job types cannot run concurrently within one ConflictKey scope. Abstract for the
    ///     same reason ConflictKey is: the guard used to compare job types, so two types that mutate one
    ///     plan worktree were admitted 12 seconds apart (CreatePr 03456 and RetryPlan 03462 on plan 00606).
    /// </summary>
    [JsonIgnore]
    public abstract JobExclusionGroup ExclusionGroup { get; }

    /// <summary>
    ///     Whether this submission deliberately bypasses <see cref="ConflictKey" /> deduplication.
    ///     Virtual with a false default: a type with no <c>--force</c> affordance simply cannot opt out,
    ///     which is the safe direction.
    /// </summary>
    [JsonIgnore]
    public virtual bool ForceDuplicate => false;

    public List<string>? WaitForJobs { get; init; }
    public string? ChatSessionId { get; init; }
}

/// <summary>
///     Where a CreatePlan was submitted from. Only <see cref="Inbox" /> submissions get a
///     crash-recovery breadcrumb: inferring that from whether an inbox path happened to be supplied
///     meant every chat, CLI and UI CreatePlan left one behind for the next restart to resurrect
///     (#2710).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<JobOrigin>))]
public enum JobOrigin
{
    /// <summary>Not stated. The safe default: no breadcrumb, so nothing to resurrect.</summary>
    Unspecified = 0,
    Inbox,
    Chat,
    Cli,
    Api,
    Ui
}

/// <summary>
///     Which set of job types cannot run concurrently within one <see cref="JobArgsBase.ConflictKey" />
///     scope. See <see cref="JobExclusionGroups" /> for the group-to-job-type-name table this pairs with.
/// </summary>
public enum JobExclusionGroup
{
    /// <summary>Not deduplicated at all. Goes with a null ConflictKey.</summary>
    None = 0,
    /// <summary>CreatePlan, keyed on the task hash rather than a plan.</summary>
    CreatePlanTask,
    /// <summary>
    ///     Everything that writes inside the plan folder: the worktree (ExecutePlan, RetryPlan,
    ///     CreatePr) and the revisions (UpdatePlan, ExpandPlan, SplitPlan). One group, not two, because
    ///     the revision writers have to exclude the worktree writers as well as each other, and they
    ///     already share the plan folder as their ConflictKey.
    /// </summary>
    PlanWorktree,
    /// <summary>CreateIssue: reads the plan, mutates GitHub, so exclusive only with itself.</summary>
    PlanIssue,
    /// <summary>SetupProject: exclusive only with itself, as today.</summary>
    ProjectSetup
}

public record CreatePlanArgs(
    string Description,
    string Project,
    int Priority = 0,
    bool Force = false,
    string? SourcePath = null,
    string? UploadSessionId = null,
    JobOrigin Origin = JobOrigin.Unspecified) : JobArgsBase
{
    public override string Type => Constants.JobTypes.CreatePlan;

    /// <summary>
    ///     The one deduplicated type with no plan to scope to, so it keys on the task itself. Shares
    ///     <see cref="InboxBreadcrumb.TaskHash" /> with the breadcrumb naming so the two paths cannot
    ///     disagree about what counts as the same request.
    /// </summary>
    public override string? ConflictKey => InboxBreadcrumb.TaskHash(Project, Description);

    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.CreatePlanTask;
    public override bool ForceDuplicate => Force;
}

public record ExecutePlanArgs(
    string FolderPath,
    string? Note = null,
    bool Force = false) : JobArgsBase
{
    public override string Type => Constants.JobTypes.ExecutePlan;
    public override string PlanFolder => FolderPath;
    public override string? ConflictKey => PlanFolder;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.PlanWorktree;
    public override bool ForceDuplicate => Force;
}

public record RetryPlanArgs(
    string FolderPath,
    string ChangeRequest,
    bool Force = false) : JobArgsBase
{
    public override string Type => Constants.JobTypes.RetryPlan;
    public override string PlanFolder => FolderPath;
    public override string? ConflictKey => PlanFolder;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.PlanWorktree;
    public override bool ForceDuplicate => Force;
}

public record ExpandPlanArgs(
    string FolderPath,
    bool Force = false) : JobArgsBase
{
    public override string Type => Constants.JobTypes.ExpandPlan;
    public override string PlanFolder => FolderPath;
    public override string? ConflictKey => PlanFolder;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.PlanWorktree;
    public override bool ForceDuplicate => Force;
}

public record UpdatePlanArgs(
    string FolderPath,
    string? Instructions = null,
    string? UploadSessionId = null,
    bool Force = false) : JobArgsBase
{
    public override string Type => Constants.JobTypes.UpdatePlan;
    public override string PlanFolder => FolderPath;
    public override string? ConflictKey => PlanFolder;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.PlanWorktree;
    public override bool ForceDuplicate => Force;
}

public record SplitPlanArgs(
    string FolderPath,
    bool Force = false) : JobArgsBase
{
    public override string Type => Constants.JobTypes.SplitPlan;
    public override string PlanFolder => FolderPath;
    public override string? ConflictKey => PlanFolder;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.PlanWorktree;
    public override bool ForceDuplicate => Force;
}

public record CreatePrArgs(
    string FolderPath,
    bool SolveMergeConflicts = true,
    bool Merge = true,
    bool DeleteBranch = true,
    bool IncludeArtifacts = true,
    string[]? Reviewers = null,
    string? Comment = null,
    bool Draft = false,
    string? BaseBranch = null,
    bool Force = false) : JobArgsBase
{
    public override string Type => Constants.JobTypes.CreatePr;
    public override string PlanFolder => FolderPath;
    public override string? ConflictKey => PlanFolder;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.PlanWorktree;
    public override bool ForceDuplicate => Force;
}

public record CreateIssueArgs(
    string FolderPath,
    string Repo,
    string? Assignee = null,
    string? Comment = null,
    string? Labels = null,
    bool Force = false) : JobArgsBase
{
    public override string Type => Constants.JobTypes.CreateIssue;
    public override string PlanFolder => FolderPath;
    public override string? ConflictKey => PlanFolder;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.PlanIssue;
    public override bool ForceDuplicate => Force;
}

public record SetupProjectArgs(
    string FolderPath) : JobArgsBase
{
    public override string Type => Constants.JobTypes.SetupProject;
    public override string PlanFolder => FolderPath;
    public override string? ConflictKey => PlanFolder;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.ProjectSetup;
}

public record SyncRepoArgs(
    string RepoPath,
    string BaseBranch,
    string? PlanFolderPath = null,
    UntrackedChangesPolicy UntrackedChangesPolicy = UntrackedChangesPolicy.Stash) : JobArgsBase
{
    public override string Type => Constants.JobTypes.SyncRepo;
    public override string? PlanFolder => PlanFolderPath;
    // Opt-out on purpose: JobService.TryFindExistingSyncRepoJob already merges a duplicate submission
    // into the existing job, which is richer than rejecting it.
    public override string? ConflictKey => null;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.None;
}

public record AddProjectArgs(
    string ProjectName,
    List<RepoRef> Repos) : JobArgsBase
{
    public override string Type => Constants.JobTypes.AddProject;
    // Opt-out on purpose: no plan scope, and no duplicate submissions of this type in the #2710
    // incident. Stated rather than inherited so the next reader knows it was considered.
    public override string? ConflictKey => null;
    public override JobExclusionGroup ExclusionGroup => JobExclusionGroup.None;
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
