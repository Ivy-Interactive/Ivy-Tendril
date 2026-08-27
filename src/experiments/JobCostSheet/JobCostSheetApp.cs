using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

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
        ("Deep profile", "An ExecutePlan job, showing the profile it ran under."),
        ("Reasoning tokens", "Reasoning shown for information and kept out of the totals."),
        ("Totals only", "A job predating the per-bucket columns: totals survive, breakdown does not."),
        ("Not costed yet", "A finished job whose cost has not landed."),
        ("Still running", "The same emptiness, worded for a job that has not finished."),
        ("Job not found", "A null model — the job was cleared while its sheet was open."),
    ];

    internal static JobCostModel? Find(string name) => name switch
    {
        "Agent-reported cost" => Standard(),
        "Computed cost" => Standard() with
        {
            Reconciliation = "Charged $2.4180, computed from the rates above (the agent did not "
                             + "report a cost).",
        },
        "Rates disagree" => Standard() with
        {
            Reconciliation = "Charged $3.1000 as reported by the claude CLI. The per-token rates "
                             + "above are Tendril's reference prices and were not used for this "
                             + "figure. Those rates would give $2.4180.",
        },
        "No price list entry" => Standard() with
        {
            Model = "some-unlisted-model",
            Buckets = Standard().Buckets.Select(b => b with { RatePerMillion = null }).ToList(),
            ComputedCost = null,
            Reconciliation = "Charged $2.4180 as reported by the claude CLI. The per-token rates "
                             + "above are Tendril's reference prices and were not used for this figure.",
            PriceListSource = "No price list entry matches 'some-unlisted-model', so no rates could "
                              + "be applied.",
        },
        "Deep profile" => Standard() with { Type = "ExecutePlan", Profile = "Deep" },
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
            Reconciliation = "Charged $1.2050. The source of this figure was not recorded (it "
                             + "predates cost-source tracking).",
        },
        "Not costed yet" => Empty() with
        {
            NoUsageReason = "No usage data recorded for this job. Cost is calculated about 30 "
                            + "seconds after a job finishes, so a just-completed job may still be "
                            + "pending.",
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
            new JobCostBucket("Cache read", 412_000, 1.50m, CountsTowardTotal: true),
            new JobCostBucket("Cache write", 22_100, 18.75m, CountsTowardTotal: true),
        ],
        TotalTokens = 481_750,
        ComputedCost = 2.4180m,
        Reconciliation = "Charged $2.4180 as reported by the claude CLI. The per-token rates above "
                         + "are Tendril's reference prices and were not used for this figure.",
        PriceListSource = "Rates for 'claude-opus-4' come from: Static catalog (claude). They are "
                          + "hardcoded in src/Ivy.Tendril.Agents/Providers/Claude/ClaudeModelCatalog.cs",
    };

    /// <summary>A job with no per-bucket usage at all — the shape the empty states build on.</summary>
    private static JobCostModel Empty() => new()
    {
        Model = "claude-opus-4",
        Provider = "claude",
        Type = "CreatePlan",
        Reconciliation = "No cost recorded for this job; the figures above are token counts only.",
        PriceListSource = "Rates for 'claude-opus-4' come from: Static catalog (claude).",
    };
}
