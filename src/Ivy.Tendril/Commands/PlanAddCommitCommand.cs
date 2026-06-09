using Ivy.Tendril.Models;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddCommitSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Commit SHA")]
    [CommandArgument(1, "<sha>")]
    public string Sha { get; set; } = "";
}

public class PlanAddCommitCommand : Command<PlanAddCommitSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanAddCommitCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanAddCommitSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        if (!Regex.IsMatch(settings.Sha, @"^[0-9a-fA-F]{7,40}$"))
            throw new ArgumentException($"Invalid commit hash format: {settings.Sha}. Expected 7-40 character hex string.");

        if (plan.Commits.Contains(settings.Sha))
        {
            Console.WriteLine($"Commit already in plan: {settings.Sha}");
            return 0;
        }

        plan.Commits.Add(settings.Sha);
        plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Added commit: {settings.Sha}");
        return 0;
    }
}
