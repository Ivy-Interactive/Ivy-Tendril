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
    [JsonIgnore]
    public abstract string Type { get; }
    [JsonIgnore]
    public virtual string? PlanFolder => null;
}

public record CreatePlanArgs(
    string Description,
    string Project,
    int Priority = 0,
    bool Force = false,
    string? SourcePath = null) : JobArgsBase
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

public record ExpandPlanArgs(
    string FolderPath) : JobArgsBase
{
    public override string Type => Constants.JobTypes.ExpandPlan;
    public override string PlanFolder => FolderPath;
}

public record UpdatePlanArgs(
    string FolderPath,
    string? Instructions = null) : JobArgsBase
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
    bool Merge = true,
    bool DeleteBranch = true,
    bool IncludeArtifacts = true,
    string? Assignee = null,
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

public record UpdateProjectArgs(
    string FolderPath) : JobArgsBase
{
    public override string Type => Constants.JobTypes.UpdateProject;
    public override string PlanFolder => FolderPath;
}
