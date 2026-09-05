namespace Ivy.Tendril.Widgets;

/// <param name="Hint">
///     A second line under the value, for a figure that is not actionable without its basis (the cost
///     forecast states the day counts it projected from here). Null on the cards that need none.
/// </param>
public record DashboardKpiDto(
    string Label,
    string Value,
    string? Delta = null,
    string? Direction = null,
    string? Hint = null);

public record DashboardMonthValueDto(string Label, double Value);

public record DashboardActivityMonthDto(string Label, List<int> Weeks);

/// <summary>Row in the Active Jobs card. Status is lowercased; "running" spins the row's loader.</summary>
public record DashboardJobDto(string Id, string PlanId, string Title, string Status);

public record DashboardTrendDto(
    List<string> Months,
    List<double> Cost,
    List<double> Plans,
    List<double?> PrevCost,
    List<double?> PrevPlans);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilDashboard",
    GlobalName = "IvyTendrilWidgets"
)]
[Slot("ProcessViewer")]
[Slot("UpdateNotice")]
[Slot("TunnelQr")]
[Slot("TunnelMenu")]
public record TendrilDashboard : WidgetBase<TendrilDashboard>
{
    public TendrilDashboard(
        object? processViewer = null,
        object? updateNotice = null,
        object? tunnelQr = null,
        object? tunnelMenu = null)
        : base(BuildSlots(processViewer, updateNotice, tunnelQr, tunnelMenu))
    {
    }

    private static object[] BuildSlots(
        object? processViewer,
        object? updateNotice,
        object? tunnelQr,
        object? tunnelMenu)
    {
        var slots = new List<object>
        {
            processViewer != null ? new Slot("ProcessViewer", processViewer) : new Slot("ProcessViewer")
        };
        if (updateNotice != null)
            slots.Add(new Slot("UpdateNotice", updateNotice));
        if (tunnelQr != null)
            slots.Add(new Slot("TunnelQr", tunnelQr));
        if (tunnelMenu != null)
            slots.Add(new Slot("TunnelMenu", tunnelMenu));
        return slots.ToArray();
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
    [Prop] public List<DashboardJobDto> Jobs { get; init; } = new();

    [Event] public EventHandler<Event<TendrilDashboard>>? OnDrafts { get; init; }
    [Event] public EventHandler<Event<TendrilDashboard>>? OnReview { get; init; }
    [Event] public EventHandler<Event<TendrilDashboard>>? OnJobs { get; init; }
    [Event] public EventHandler<Event<TendrilDashboard, string>>? OnJob { get; init; }
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

    public static TendrilDashboard Jobs(this TendrilDashboard w, List<DashboardJobDto> value) =>
        w with { Jobs = value };

    public static TendrilDashboard OnDrafts(this TendrilDashboard w, Action handler) =>
        w with { OnDrafts = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilDashboard OnReview(this TendrilDashboard w, Action handler) =>
        w with { OnReview = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilDashboard OnJobs(this TendrilDashboard w, Action handler) =>
        w with { OnJobs = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilDashboard OnJob(this TendrilDashboard w, Action<string> handler) =>
        w with { OnJob = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };
}
