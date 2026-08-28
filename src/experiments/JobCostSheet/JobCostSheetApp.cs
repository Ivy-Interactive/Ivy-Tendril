using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;

namespace JobCostSheetDemo;

/// <summary>
///     Opens <see cref="JobCostSheet" /> in each state it can reach, without a job service, a
///     database or a price list behind it.
///     <para>
///         That is the point of the sheet taking a <see cref="JobCostModel" />: every state below is
///         a literal, so the ones that are awkward to reproduce against real data — a cost the agent
///         reported that disagrees with the rates, a job old enough to predate the per-bucket
///         columns — are as easy to look at as the ordinary one.
///     </para>
/// </summary>
[App(title: "Job Cost Sheet", icon: Icons.Receipt)]
public class JobCostSheetApp : ViewBase
{
    public override object Build()
    {
        var (sheet, showSheet) = UseTrigger<string>((isOpen, scenario) =>
        {
            if (!isOpen.Value) return null;

            return new Sheet(
                () => isOpen.Set(false),
                new JobCostSheet(Scenarios.Find(scenario)),
                "Cost & Tokens"
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        var buttons = Layout.Vertical().Gap(2);
        foreach (var (name, description) in Scenarios.All)
        {
            buttons |= Layout.Horizontal().Gap(3).AlignContent(Align.Left)
                       | new Button(name).Outline().Width(Size.Units(60)).OnClick(() => showSheet(name))
                       | Text.Muted(description);
        }

        return Layout.Vertical().Gap(6).Padding(6)
               | Text.H3("Job cost sheet")
               | Text.Muted("Each button opens the sheet against a hand-built model. Nothing here "
                            + "reads a job, a plan or a price list.")
               | buttons
               | sheet;
    }
}

/// <summary>The states worth looking at, and the model that produces each one.</summary>
internal static class Scenarios
{
    internal static readonly (string Name, string Description)[] All =
    [
        ("Agent-reported cost", "The ordinary case: full breakdown, charge as the CLI reported it."),
        ("Computed cost", "The agent reported no cost, so the rates in the table produced it."),
        ("Rates disagree", "An agent-reported charge the reference rates do not reproduce."),
        ("No price list entry", "A model the price list does not cover — tokens, but no rates."),
        ("Refreshed from models.dev", "Rates refreshed from models.dev, so the row links out."),
        ("Deep profile", "An ExecutePlan job, showing the profile and effort it ran under."),
        ("Reasoning tokens", "Reasoning shown for information and kept out of the totals."),
        ("Totals only", "A job predating the per-bucket columns: an empty table carrying its totals."),
        ("Not costed yet", "A finished job whose cost has not landed."),
        ("Still running", "The same emptiness, worded for a job that has not finished."),
        ("Job not found", "A null model — the job was cleared while its sheet was open."),
    ];

    internal static JobCostModel? Find(string name) => name switch
    {
        "Agent-reported cost" => Standard(),
        "Computed cost" => Standard() with
        {
            AgentReportedCost = null,
            CostSource = JobCostSources.Computed,
        },
        "Rates disagree" => Standard() with { AgentReportedCost = 3.1000m, ChargedCost = 3.1000m },
        "No price list entry" => Standard() with
        {
            Model = "some-unlisted-model",
            Buckets = Standard().Buckets.Select(b => b with { RatePerMillion = null }).ToList(),
            ComputedCost = null,
            PriceList = "",
            PriceListUrl = null,
        },
        "Refreshed from models.dev" => Standard() with
        {
            PriceList = "models.dev",
            PriceListUrl = "https://models.dev/",
        },
        "Deep profile" => Standard() with { Type = "ExecutePlan", Profile = "Deep", Effort = "High" },
        "Reasoning tokens" => Standard() with
        {
            Buckets =
            [
                .. Standard().Buckets,
                new JobCostBucket("Reasoning", 12_400, null, CountsTowardTotal: false),
            ],
        },
        "Totals only" => Empty() with
        {
            TotalsOnlyTokens = 48_300,
            TotalsOnlyCost = 1.2050m,
            ChargedCost = 1.2050m,
            CostSource = null,
        },
        "Not costed yet" => Empty() with
        {
            NoUsageReason = "No usage data recorded for this job. Cost is calculated about 30 "
                            + "seconds after a job finishes.",
        },
        "Still running" => Empty() with
        {
            NoUsageReason = "No usage data recorded for this job yet. Cost is calculated about 30 "
                            + "seconds after the job finishes.",
        },
        _ => null,
    };

    /// <summary>A completed job with a full breakdown and rates that match the charge.</summary>
    private static JobCostModel Standard() => new()
    {
        Model = "claude-opus-4",
        Provider = "claude",
        Type = "CreatePlan",
        Buckets =
        [
            new JobCostBucket("Input", 38_200, 15m, CountsTowardTotal: true),
            new JobCostBucket("Output", 9_450, 75m, CountsTowardTotal: true),
            new JobCostBucket("Cache Read", 412_000, 1.50m, CountsTowardTotal: true),
            new JobCostBucket("Cache Write", 22_100, 18.75m, CountsTowardTotal: true),
        ],
        TotalTokens = 481_750,
        ComputedCost = 2.4180m,
        AgentReportedCost = 2.4180m,
        ChargedCost = 2.4180m,
        CostSource = JobCostSources.Agent,
        PriceList = "Tendril",
    };

    /// <summary>A job with no per-bucket usage at all - the shape the empty states build on.</summary>
    private static JobCostModel Empty() => new()
    {
        Model = "claude-opus-4",
        Provider = "claude",
        Type = "CreatePlan",
        PriceList = "Tendril",
    };
}
