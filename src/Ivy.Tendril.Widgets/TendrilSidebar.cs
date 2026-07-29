using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;

namespace Ivy.Tendril.Widgets;

public record JobSubItem(string Id, string Name, int? Count = null);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilSidebar",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilSidebar : WidgetBase<TendrilSidebar>
{
    [Prop] public string Version { get; init; } = "v 1.0.20";
    [Prop] public string AgentName { get; init; } = "Claude Code";
    [Prop] public string AgentShortcut { get; init; } = "⌘ A";
    [Prop] public string NewPlanShortcut { get; init; } = "⌘ K";
    [Prop] public string ActiveItem { get; init; } = "dashboard";
    [Prop] public int DraftCount { get; init; }
    [Prop] public int ReviewCount { get; init; }
    [Prop] public int RecommendationsCount { get; init; }
    [Prop] public int JobCount { get; init; }
    [Prop] public JobSubItem[] Jobs { get; init; } = [];
    [Prop] public int PullRequestCount { get; init; }
    [Prop] public int IceboxCount { get; init; }
    [Prop] public int HelpRequestCount { get; init; }
    [Prop] public bool Collapsed { get; init; }
    [Prop] public bool ShowCollapseButton { get; init; }

    [Event] public EventHandler<Event<TendrilSidebar, string>>? OnSelect { get; init; }
    [Event] public EventHandler<Event<TendrilSidebar>>? OnNewPlan { get; init; }
    [Event] public EventHandler<Event<TendrilSidebar>>? OnSelectAgent { get; init; }
    [Event] public EventHandler<Event<TendrilSidebar>>? OnToggleCollapse { get; init; }
}

public static class TendrilSidebarExtensions
{
    public static TendrilSidebar Version(this TendrilSidebar w, string version) =>
        w with { Version = version };

    public static TendrilSidebar AgentName(this TendrilSidebar w, string agentName) =>
        w with { AgentName = agentName };

    public static TendrilSidebar AgentShortcut(this TendrilSidebar w, string shortcut) =>
        w with { AgentShortcut = shortcut };

    public static TendrilSidebar NewPlanShortcut(this TendrilSidebar w, string shortcut) =>
        w with { NewPlanShortcut = shortcut };

    public static TendrilSidebar ActiveItem(this TendrilSidebar w, string activeItem) =>
        w with { ActiveItem = activeItem };

    public static TendrilSidebar DraftCount(this TendrilSidebar w, int count) =>
        w with { DraftCount = count };

    public static TendrilSidebar ReviewCount(this TendrilSidebar w, int count) =>
        w with { ReviewCount = count };

    public static TendrilSidebar RecommendationsCount(this TendrilSidebar w, int count) =>
        w with { RecommendationsCount = count };

    public static TendrilSidebar JobCount(this TendrilSidebar w, int count) =>
        w with { JobCount = count };

    public static TendrilSidebar Jobs(this TendrilSidebar w, params JobSubItem[] jobs) =>
        w with { Jobs = jobs };

    public static TendrilSidebar Jobs(this TendrilSidebar w, IEnumerable<JobSubItem> jobs) =>
        w with { Jobs = jobs.ToArray() };

    public static TendrilSidebar PullRequestCount(this TendrilSidebar w, int count) =>
        w with { PullRequestCount = count };

    public static TendrilSidebar IceboxCount(this TendrilSidebar w, int count) =>
        w with { IceboxCount = count };

    public static TendrilSidebar HelpRequestCount(this TendrilSidebar w, int count) =>
        w with { HelpRequestCount = count };

    public static TendrilSidebar Collapsed(this TendrilSidebar w, bool collapsed = true) =>
        w with { Collapsed = collapsed };

    public static TendrilSidebar ShowCollapseButton(this TendrilSidebar w, bool show = true) =>
        w with { ShowCollapseButton = show };

    public static TendrilSidebar OnSelect(this TendrilSidebar w, Action<string> handler) =>
        w with
        {
            OnSelect = new(e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            })
        };

    public static TendrilSidebar OnNewPlan(this TendrilSidebar w, Action handler) =>
        w with
        {
            OnNewPlan = new(_ =>
            {
                handler();
                return ValueTask.CompletedTask;
            })
        };

    public static TendrilSidebar OnSelectAgent(this TendrilSidebar w, Action handler) =>
        w with
        {
            OnSelectAgent = new(_ =>
            {
                handler();
                return ValueTask.CompletedTask;
            })
        };

    public static TendrilSidebar OnToggleCollapse(this TendrilSidebar w, Action handler) =>
        w with
        {
            OnToggleCollapse = new(_ =>
            {
                handler();
                return ValueTask.CompletedTask;
            })
        };
}
