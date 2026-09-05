using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanRemovePrSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("PR URL (e.g., https://github.com/org/repo/pull/123)")]
    [CommandArgument(1, "<pr-url>")]
    public string PrUrl { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(PrUrl, "pr-url"));
    }
}

/// <summary>
///     The repair half of `plan add-pr`. A PR URL that does not belong to a plan has to be removable
///     without hand editing plan.yaml, because a foreign URL in a dependency's Prs list blocks every
///     plan that depends on it (issue #2336). Matching is on the canonical owner/repo#number, so a URL
///     recorded with a /files suffix or a fragment can still be removed by its base form.
/// </summary>
public class PlanRemovePrCommand : Command<PlanRemovePrSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanRemovePrCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRemovePrSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);

        RemovePr(planFolder, settings.PrUrl, _planWatcher);

        Console.WriteLine($"Removed PR: {settings.PrUrl}");
        return 0;
    }

    /// <summary>
    ///     Read, remove, write. Separated from <see cref="Execute" /> so it can be exercised without a
    ///     Spectre command context.
    /// </summary>
    internal static void RemovePr(string planFolder, string prUrl, IPlanWatcherService? planWatcher = null)
    {
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        var removed = plan.Prs.RemoveAll(p => PrUrlHelper.SamePr(p, prUrl));
        if (removed == 0)
            throw new InvalidOperationException($"PR not found: {prUrl}");

        plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, planWatcher);
    }
}
