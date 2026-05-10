using System.Text.Json.Serialization;

namespace Ivy.Tendril.Models;

[JsonDerivedType(typeof(CreatePlanArgs), "CreatePlan")]
[JsonDerivedType(typeof(ExecutePlanArgs), "ExecutePlan")]
[JsonDerivedType(typeof(ExpandPlanArgs), "ExpandPlan")]
[JsonDerivedType(typeof(UpdatePlanArgs), "UpdatePlan")]
[JsonDerivedType(typeof(SplitPlanArgs), "SplitPlan")]
[JsonDerivedType(typeof(CreatePrArgs), "CreatePr")]
[JsonDerivedType(typeof(CreateIssueArgs), "CreateIssue")]
[JsonDerivedType(typeof(UpdateProjectArgs), "UpdateProject")]
public abstract record JobArgsBase
{
    public virtual string? PlanFolder => null;
}

public record CreatePlanArgs(
    string Description,
    string Project,
    int Priority = 0,
    bool Force = false,
    string? SourcePath = null) : JobArgsBase;

public record ExecutePlanArgs(
    string FolderPath,
    string? Note = null) : JobArgsBase
{
    public override string PlanFolder => FolderPath;
}

public record ExpandPlanArgs(
    string FolderPath) : JobArgsBase
{
    public override string PlanFolder => FolderPath;
}

public record UpdatePlanArgs(
    string FolderPath) : JobArgsBase
{
    public override string PlanFolder => FolderPath;
}

public record SplitPlanArgs(
    string FolderPath) : JobArgsBase
{
    public override string PlanFolder => FolderPath;
}

public record CreatePrArgs(
    string FolderPath,
    bool Merge = true,
    bool DeleteBranch = true,
    bool IncludeArtifacts = true,
    string? Assignee = null,
    string? Comment = null,
    bool Draft = false) : JobArgsBase
{
    public override string PlanFolder => FolderPath;
}

public record CreateIssueArgs(
    string FolderPath,
    string Repo,
    string? Assignee = null,
    string? Comment = null,
    string? Labels = null) : JobArgsBase
{
    public override string PlanFolder => FolderPath;
}

public record UpdateProjectArgs(
    string FolderPath) : JobArgsBase
{
    public override string PlanFolder => FolderPath;
}
