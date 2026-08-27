using System.Globalization;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Apps.Views.Sheets;

/// <summary>
/// Breaks one job's Cost and Tokens cells down into the buckets behind them: how many tokens of
/// each kind, what per-million rate applies, what that arithmetic comes to — and, crucially,
/// whether the charge actually came from that arithmetic or was reported by the agent CLI.
/// <para>
/// Renders a <see cref="JobCostModel" /> and nothing else: every lookup, sum and finding is settled
/// by <see cref="JobCostModelBuilder" /> before it gets here. Use <see cref="JobCostSheetView" /> to
/// render one by job id and keep it live.
/// </para>
/// </summary>
public class JobCostSheet(JobCostModel? model) : ViewBase
{
    private const string NoValue = "—";

    /// <summary>
    /// Every number here is presented as US dollars or a raw token count, so it is formatted with
    /// the invariant culture — see <see cref="FormatHelper" />, which is where the Jobs UI's dollar
    /// formatting lives.
    /// </summary>
    private static string Usd(decimal value) => FormatHelper.FormatCost(value, decimals: 4);

    /// <summary>A per-million rate, grouped: "$1,234.56".</summary>
    private static string Rate(decimal value) => "$" + value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Count(int value) => FormatHelper.FormatCount(value);

    public override object Build()
    {
        if (model is null)
            return Text.P("Job not found.");

        var details = new
        {
            model.Model,
            model.Provider,
            model.Type,
            // The profile the job ran under, when its type takes one — absent rows are dropped below.
            Profile = model.Profile ?? "",
        }.ToDetails().RemoveEmpty();

        if (!model.HasBreakdown)
            return Layout.Vertical().Gap(4)
                   | details
                   | BuildNoBreakdownView(model);

        var table = model.Buckets
            .Select(b => new UsageRow
            {
                Kind = b.Kind,
                Tokens = Count(b.Tokens),
                RatePerMillion = b.RatePerMillion.HasValue ? Rate(b.RatePerMillion.Value) : NoValue,
                Cost = b.RatePerMillion.HasValue ? Usd(b.Tokens * b.RatePerMillion.Value / 1_000_000m) : NoValue,
            })
            .ToTable()
            .Header(x => x.Kind, "Token type")
            .Header(x => x.Tokens, "Tokens")
            .Header(x => x.RatePerMillion, "Rate / 1M")
            .Header(x => x.Cost, "Cost")
            .AlignContent(x => x.Tokens, Align.Right)
            .AlignContent(x => x.RatePerMillion, Align.Right)
            .AlignContent(x => x.Cost, Align.Right)
            .Totals(x => x.Kind, _ => "Total")
            .Totals(x => x.Tokens, _ => Count(model.TotalTokens))
            .Totals(x => x.Cost, _ => model.ComputedCost.HasValue ? Usd(model.ComputedCost.Value) : NoValue)
            .Width(Size.Full());

        return Layout.Vertical().Gap(4)
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Breakdown")
                  | table
                  | Text.Muted(model.Reconciliation))
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Price list")
                  | Text.Muted(model.PriceListSource)
                  | Text.Muted(
                      "The Tokens column in the Jobs table counts input + output only; cache tokens "
                      + "are listed here but excluded from it. Reasoning tokens are shown for "
                      + "information and excluded from the totals above to avoid double-counting "
                      + "output."));
    }

    /// <summary>
    /// Shown when no per-bucket data was persisted: either the job has not been costed yet, or it
    /// predates the breakdown columns — in which case the totals it does have are still worth showing.
    /// </summary>
    private static object BuildNoBreakdownView(JobCostModel model)
    {
        if (model.NoUsageReason is not null)
            return Text.Muted(model.NoUsageReason);

        var totals = new List<string>();
        if (model.TotalsOnlyTokens is > 0) totals.Add($"{Count(model.TotalsOnlyTokens.Value)} tokens (input + output)");
        if (model.TotalsOnlyCost is not null) totals.Add(Usd(model.TotalsOnlyCost.Value));

        return Text.Block(string.Join(" · ", totals));
    }

    private sealed record UsageRow
    {
        public required string Kind { get; init; }
        public required string Tokens { get; init; }
        public required string RatePerMillion { get; init; }
        public required string Cost { get; init; }
    }
}

/// <summary>
/// Renders <see cref="JobCostSheet" /> for a job id and keeps it current.
/// <para>
/// Cost lands about 30 seconds after a job finishes, so a sheet opened on a just-completed job has
/// to pick it up rather than sit on the empty state until it is reopened. Subscribing is why this
/// exists as a view: the sheet itself is a pure render of an already-resolved model, and the call
/// sites build theirs inside a trigger lambda where a hook cannot go.
/// </para>
/// </summary>
public class JobCostSheetView(string jobId, IJobService jobService) : ViewBase
{
    public override object Build()
    {
        var pricingProvider = UseService<IModelPricingProvider>();

        Context.UseJobUpdates(jobService, jobId, JobCostModelBuilder.BuildSignature);

        return new JobCostSheet(JobCostModelBuilder.Build(jobId, jobService, pricingProvider));
    }
}
