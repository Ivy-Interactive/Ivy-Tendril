namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilProcessViewer",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilProcessViewer : WidgetBase<TendrilProcessViewer>
{
    [Prop] public int DraftCount { get; init; }
    [Prop] public int ReviewCount { get; init; }
    [Prop] public int CreatingPlansCount { get; init; }
    [Prop] public int UpdatingPlansCount { get; init; }
    [Prop] public int ExecutingPlansCount { get; init; }
    [Prop] public int RetryingPlansCount { get; init; }
    [Prop] public int CreatingPrCount { get; init; }

    [Event] public EventHandler<Event<TendrilProcessViewer>>? OnCreate { get; init; }
    [Event] public EventHandler<Event<TendrilProcessViewer>>? OnDrafts { get; init; }
    [Event] public EventHandler<Event<TendrilProcessViewer>>? OnReview { get; init; }
    [Event] public EventHandler<Event<TendrilProcessViewer>>? OnJobs { get; init; }
}

public static class TendrilProcessViewerExtensions
{
    public static TendrilProcessViewer DraftCount(this TendrilProcessViewer w, int count) =>
        w with { DraftCount = count };

    public static TendrilProcessViewer ReviewCount(this TendrilProcessViewer w, int count) =>
        w with { ReviewCount = count };

    public static TendrilProcessViewer CreatingPlansCount(this TendrilProcessViewer w, int count) =>
        w with { CreatingPlansCount = count };

    public static TendrilProcessViewer UpdatingPlansCount(this TendrilProcessViewer w, int count) =>
        w with { UpdatingPlansCount = count };

    public static TendrilProcessViewer ExecutingPlansCount(this TendrilProcessViewer w, int count) =>
        w with { ExecutingPlansCount = count };

    public static TendrilProcessViewer RetryingPlansCount(this TendrilProcessViewer w, int count) =>
        w with { RetryingPlansCount = count };

    public static TendrilProcessViewer CreatingPrCount(this TendrilProcessViewer w, int count) =>
        w with { CreatingPrCount = count };

    public static TendrilProcessViewer OnCreate(this TendrilProcessViewer w, Action handler) =>
        w with { OnCreate = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilProcessViewer OnDrafts(this TendrilProcessViewer w, Action handler) =>
        w with { OnDrafts = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilProcessViewer OnReview(this TendrilProcessViewer w, Action handler) =>
        w with { OnReview = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilProcessViewer OnJobs(this TendrilProcessViewer w, Action handler) =>
        w with { OnJobs = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}
