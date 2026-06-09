using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;

namespace Ivy.Widgets.TendrilProcess;

[ExternalWidget(
    "Widgets/frontend/dist/ivy-tendril-widgets.js",
    StylePath = "Widgets/frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilProcess",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilProcess : WidgetBase<TendrilProcess>
{
    [Prop] public int DraftCount { get; init; }
    [Prop] public int ReviewCount { get; init; }
    [Prop] public int CreatingPlansCount { get; init; }
    [Prop] public int UpdatingPlansCount { get; init; }
    [Prop] public int ExecutingPlansCount { get; init; }
    [Prop] public int RetryingPlansCount { get; init; }
    [Prop] public int CreatingPrCount { get; init; }

    [Event] public EventHandler<Event<TendrilProcess>>? OnCreate { get; init; }
    [Event] public EventHandler<Event<TendrilProcess>>? OnDrafts { get; init; }
    [Event] public EventHandler<Event<TendrilProcess>>? OnReview { get; init; }
    [Event] public EventHandler<Event<TendrilProcess>>? OnJobs { get; init; }
}

public static class TendrilProcessExtensions
{
    public static TendrilProcess DraftCount(this TendrilProcess w, int count) =>
        w with { DraftCount = count };

    public static TendrilProcess ReviewCount(this TendrilProcess w, int count) =>
        w with { ReviewCount = count };

    public static TendrilProcess CreatingPlansCount(this TendrilProcess w, int count) =>
        w with { CreatingPlansCount = count };

    public static TendrilProcess UpdatingPlansCount(this TendrilProcess w, int count) =>
        w with { UpdatingPlansCount = count };

    public static TendrilProcess ExecutingPlansCount(this TendrilProcess w, int count) =>
        w with { ExecutingPlansCount = count };

    public static TendrilProcess RetryingPlansCount(this TendrilProcess w, int count) =>
        w with { RetryingPlansCount = count };

    public static TendrilProcess CreatingPrCount(this TendrilProcess w, int count) =>
        w with { CreatingPrCount = count };

    public static TendrilProcess OnCreate(this TendrilProcess w, Action handler) =>
        w with { OnCreate = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilProcess OnDrafts(this TendrilProcess w, Action handler) =>
        w with { OnDrafts = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilProcess OnReview(this TendrilProcess w, Action handler) =>
        w with { OnReview = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilProcess OnJobs(this TendrilProcess w, Action handler) =>
        w with { OnJobs = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}
