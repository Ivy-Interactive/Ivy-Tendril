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
    internal static JobArgsBase? FromLegacy(string type, string[] args)
    {
        if (args.Length == 0) return null;

        return type switch
        {
            Constants.JobTypes.CreatePlan => new CreatePlanArgs(
                GetNamedArg(args, "-Description") ?? string.Join(" ", args),
                GetNamedArg(args, "-Project") ?? "Auto",
                int.TryParse(GetNamedArg(args, "-Priority"), out var p) ? p : 0,
                args.Contains("-Force", StringComparer.OrdinalIgnoreCase),
                GetNamedArg(args, "-SourcePath")),
            Constants.JobTypes.ExecutePlan => new ExecutePlanArgs(args[0], GetNamedArg(args, "-Note")),
            Constants.JobTypes.ExpandPlan => new ExpandPlanArgs(args[0]),
            Constants.JobTypes.UpdatePlan => new UpdatePlanArgs(args[0]),
            Constants.JobTypes.SplitPlan => new SplitPlanArgs(args[0]),
            Constants.JobTypes.CreatePr => new CreatePrArgs(args[0]),
            Constants.JobTypes.CreateIssue => new CreateIssueArgs(
                args[0],
                GetNamedArg(args, "-Repo") ?? "",
                GetNamedArg(args, "-Assignee"),
                GetNamedArg(args, "-Comment"),
                GetNamedArg(args, "-Labels")),
            Constants.JobTypes.UpdateProject => new UpdateProjectArgs(args[0]),
            _ => null
        };
    }

    private static string? GetNamedArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
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
    string FolderPath) : JobArgsBase
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
