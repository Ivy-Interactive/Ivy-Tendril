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

    /// <summary>A details value, or the em-dash the table already uses for "nothing here".</summary>
    private static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? NoValue : value;

    public override object Build()
    {
        if (model is null)
            return Callout.Info(
                "This job no longer exists — it was cleared while this sheet was open.",
                "Job Not Found");

        // Every row is kept, dashed when it has no value: which facts the sheet reports is then the
        // same for every job, and a blank Effort reads as "none recorded" rather than as a row the
        // reader has to notice is missing.
        var details = new
        {
            Model = Dash(model.Model),
            Provider = Dash(model.Provider),
            Type = Dash(model.Type),
            Profile = Dash(model.Profile),
            Effort = Dash(model.Effort),
            CostReportedByAgent = BuildAgentCostValue(model),
            PriceList = Dash(model.PriceList),
        }
            .ToDetails()
            .Label(x => x.CostReportedByAgent, "Cost Reported by Agent")
            .Label(x => x.PriceList, "Price List")
            // Tendril's catalogs are hardcoded and have nowhere to point; models.dev does.
            .Builder(x => x.PriceList, f => f.Func((string name) => model.PriceListUrl is null
                ? Text.Block(name)
                : new Button(name, variant: ButtonVariant.Inline).Url(model.PriceListUrl).Target(LinkTarget.Blank)));

        if (model.NoUsageReason is not null)
            return Layout.Vertical().Gap(4)
                   | details
                   | Callout.Info(model.NoUsageReason, "No Data Recorded");

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
            .Totals(x => x.Tokens, _ => Count(model.HasBreakdown ? model.TotalTokens : model.TotalsOnlyTokens ?? 0))
            .Totals(x => x.Cost, _ => TotalCost(model))
            .Width(Size.Full());

        return Layout.Vertical().Gap(4)
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Breakdown")
                  | table);
    }

    /// <summary>
    /// What the agent's own CLI charged, when it charged anything. The table's total is what
    /// Tendril's rates come to, so showing the agent's figure beside it lets the two be compared
    /// without a sentence explaining which one won.
    /// </summary>
    private static string BuildAgentCostValue(JobCostModel model) =>
        // Everything else - a charge computed from the rates, one whose source predates cost-source
        // tracking, no charge at all - comes to the same thing for a reader: the agent gave no figure.
        model.AgentReportedCost is { } reported ? Usd(reported) : "Not Provided";

    /// <summary>
    /// The table's total: what the rates come to for a job with a breakdown, and the charge itself
    /// for one that predates the per-bucket columns and has nothing to compute from.
    /// </summary>
    private static string TotalCost(JobCostModel model)
    {
        var total = model.HasBreakdown ? model.ComputedCost : model.TotalsOnlyCost;
        return total.HasValue ? Usd(total.Value) : NoValue;
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
