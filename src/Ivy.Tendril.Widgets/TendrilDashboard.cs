namespace Ivy.Tendril.Widgets;

public record DashboardKpiDto(string Label, string Value, string? Delta = null, string? Direction = null);

public record DashboardMonthValueDto(string Label, double Value);

public record DashboardActivityMonthDto(string Label, List<int> Weeks);

public record DashboardTrendDto(
    List<string> Months,
    List<double> CostThisYear,
    List<double> CostLastYear,
    List<double> PlansThisYear,
    List<double> PlansLastYear);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilDashboard",
    GlobalName = "IvyTendrilWidgets"
)]
[Slot("ProcessViewer")]
public record TendrilDashboard : WidgetBase<TendrilDashboard>
{
    public TendrilDashboard(object? processViewer = null)
        : base(processViewer != null ? [new Slot("ProcessViewer", processViewer)] : [new Slot("ProcessViewer")])
    {
    }

    [Prop] public string DateText { get; init; } = "";
    [Prop] public string Greeting { get; init; } = "";
    [Prop] public string Headline { get; init; } = "";
    [Prop] public int DraftCount { get; init; }
    [Prop] public int InProgressCount { get; init; }
    [Prop] public int ReviewCount { get; init; }
    [Prop] public int CompletedCount { get; init; }
    [Prop] public int FailedCount { get; init; }
    [Prop] public List<DashboardKpiDto> Kpis { get; init; } = new();
    [Prop] public DashboardTrendDto? Trend { get; init; }
    [Prop] public List<DashboardMonthValueDto> PullRequests { get; init; } = new();
    [Prop] public List<DashboardActivityMonthDto> Activity { get; init; } = new();
}

public static class TendrilDashboardExtensions
{
    public static TendrilDashboard DateText(this TendrilDashboard w, string value) =>
        w with { DateText = value };

    public static TendrilDashboard Greeting(this TendrilDashboard w, string value) =>
        w with { Greeting = value };

    public static TendrilDashboard Headline(this TendrilDashboard w, string value) =>
        w with { Headline = value };

    public static TendrilDashboard DraftCount(this TendrilDashboard w, int value) =>
        w with { DraftCount = value };

    public static TendrilDashboard InProgressCount(this TendrilDashboard w, int value) =>
        w with { InProgressCount = value };

    public static TendrilDashboard ReviewCount(this TendrilDashboard w, int value) =>
        w with { ReviewCount = value };

    public static TendrilDashboard CompletedCount(this TendrilDashboard w, int value) =>
        w with { CompletedCount = value };

    public static TendrilDashboard FailedCount(this TendrilDashboard w, int value) =>
        w with { FailedCount = value };

    public static TendrilDashboard Kpis(this TendrilDashboard w, List<DashboardKpiDto> value) =>
        w with { Kpis = value };

    public static TendrilDashboard Trend(this TendrilDashboard w, DashboardTrendDto? value) =>
        w with { Trend = value };

    public static TendrilDashboard PullRequests(this TendrilDashboard w, List<DashboardMonthValueDto> value) =>
        w with { PullRequests = value };

    public static TendrilDashboard Activity(this TendrilDashboard w, List<DashboardActivityMonthDto> value) =>
        w with { Activity = value };
}
