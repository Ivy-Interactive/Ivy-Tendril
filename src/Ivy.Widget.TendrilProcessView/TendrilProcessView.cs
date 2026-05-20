using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;

namespace Ivy.Widget.TendrilProcessView;

[ExternalWidget(
    "frontend/dist/Ivy_Widget_TendrilProcessView.js",
    StylePath = "frontend/dist/ivy-widget-tendrilprocessview.css",
    ExportName = "TendrilProcessView",
    GlobalName = "Ivy_Widget_TendrilProcessView"
)]
public record TendrilProcessView : WidgetBase<TendrilProcessView>
{
    [Prop] public int DraftCount { get; init; }
    [Prop] public int ReviewCount { get; init; }
    [Prop] public int CreatingPlansCount { get; init; }
    [Prop] public int UpdatingPlansCount { get; init; }
    [Prop] public int ExecutingPlansCount { get; init; }
    [Prop] public int RetryingPlansCount { get; init; }

    [Event] public EventHandler<Event<TendrilProcessView>>? OnCreate { get; init; }
    [Event] public EventHandler<Event<TendrilProcessView>>? OnDrafts { get; init; }
    [Event] public EventHandler<Event<TendrilProcessView>>? OnReview { get; init; }
    [Event] public EventHandler<Event<TendrilProcessView>>? OnJobs { get; init; }
}

public static class TendrilProcessViewExtensions
{
    public static TendrilProcessView DraftCount(this TendrilProcessView w, int count) =>
        w with { DraftCount = count };

    public static TendrilProcessView ReviewCount(this TendrilProcessView w, int count) =>
        w with { ReviewCount = count };

    public static TendrilProcessView CreatingPlansCount(this TendrilProcessView w, int count) =>
        w with { CreatingPlansCount = count };

    public static TendrilProcessView UpdatingPlansCount(this TendrilProcessView w, int count) =>
        w with { UpdatingPlansCount = count };

    public static TendrilProcessView ExecutingPlansCount(this TendrilProcessView w, int count) =>
        w with { ExecutingPlansCount = count };

    public static TendrilProcessView RetryingPlansCount(this TendrilProcessView w, int count) =>
        w with { RetryingPlansCount = count };

    public static TendrilProcessView OnCreate(this TendrilProcessView w, Action handler) =>
        w with { OnCreate = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilProcessView OnDrafts(this TendrilProcessView w, Action handler) =>
        w with { OnDrafts = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilProcessView OnReview(this TendrilProcessView w, Action handler) =>
        w with { OnReview = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TendrilProcessView OnJobs(this TendrilProcessView w, Action handler) =>
        w with { OnJobs = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}
