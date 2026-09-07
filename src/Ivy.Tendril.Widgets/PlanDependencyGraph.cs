namespace Ivy.Tendril.Widgets;

/// <param name="Id">Plan identity, echoed back by <see cref="PlanDependencyGraph.OnNodeClick"/>.</param>
/// <param name="Badge">Short label under the title. Defaults to the id when null.</param>
public record PlanDependencyNodeDto(
    string Id,
    string Title,
    string? Status = null,
    string? Project = null,
    string? Level = null,
    string? Badge = null);

/// <summary><paramref name="Plan"/> depends on <paramref name="DependsOn"/>, which is drawn above it.</summary>
public record PlanDependencyEdgeDto(string Plan, string DependsOn);

/// <summary>
/// Layered graph of plans and the plans they depend on. Prerequisites sit at the top (or on the
/// left when horizontal) and the arrows point at what they unblock. Cycles, which a hand-edited
/// plan.yaml can always introduce, are drawn dashed rather than dropped.
/// </summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "PlanDependencyGraph",
    GlobalName = "IvyTendrilWidgets"
)]
public record PlanDependencyGraph : WidgetBase<PlanDependencyGraph>
{
    [Prop] public List<PlanDependencyNodeDto> Nodes { get; init; } = new();
    [Prop] public List<PlanDependencyEdgeDto> Edges { get; init; } = new();

    /// <summary>Plan drawn with the selection ring, typically the one open in the host app.</summary>
    [Prop] public string? SelectedId { get; init; }

    /// <summary>"vertical" (default) or "horizontal".</summary>
    [Prop] public string Orientation { get; init; } = "vertical";

    [Prop] public bool ShowLegend { get; init; } = true;
    [Prop] public string? EmptyMessage { get; init; }

    [Event] public EventHandler<Event<PlanDependencyGraph, string>>? OnNodeClick { get; init; }
}

public static class PlanDependencyGraphExtensions
{
    public static PlanDependencyGraph Nodes(this PlanDependencyGraph w, params PlanDependencyNodeDto[] nodes) =>
        w with { Nodes = nodes.ToList() };

    public static PlanDependencyGraph Nodes(this PlanDependencyGraph w, IEnumerable<PlanDependencyNodeDto> nodes) =>
        w with { Nodes = nodes.ToList() };

    public static PlanDependencyGraph Edges(this PlanDependencyGraph w, params PlanDependencyEdgeDto[] edges) =>
        w with { Edges = edges.ToList() };

    public static PlanDependencyGraph Edges(this PlanDependencyGraph w, IEnumerable<PlanDependencyEdgeDto> edges) =>
        w with { Edges = edges.ToList() };

    /// <summary>Expands one "plan id to the ids it depends on" map into the edge list.</summary>
    public static PlanDependencyGraph Dependencies(
        this PlanDependencyGraph w,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> dependsOn) =>
        w with
        {
            Edges = dependsOn
                .SelectMany(pair => pair.Value.Select(dep => new PlanDependencyEdgeDto(pair.Key, dep)))
                .ToList()
        };

    public static PlanDependencyGraph SelectedId(this PlanDependencyGraph w, string? id) =>
        w with { SelectedId = id };

    public static PlanDependencyGraph Orientation(this PlanDependencyGraph w, string orientation) =>
        w with { Orientation = orientation };

    public static PlanDependencyGraph Horizontal(this PlanDependencyGraph w) =>
        w with { Orientation = "horizontal" };

    public static PlanDependencyGraph Vertical(this PlanDependencyGraph w) =>
        w with { Orientation = "vertical" };

    public static PlanDependencyGraph ShowLegend(this PlanDependencyGraph w, bool show = true) =>
        w with { ShowLegend = show };

    public static PlanDependencyGraph EmptyMessage(this PlanDependencyGraph w, string? message) =>
        w with { EmptyMessage = message };

    public static PlanDependencyGraph OnNodeClick(this PlanDependencyGraph w, Action<string> handler) =>
        w with { OnNodeClick = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };

    public static PlanDependencyGraph OnNodeClickAsync(
        this PlanDependencyGraph w,
        Func<string, ValueTask> handler) =>
        w with { OnNodeClick = new(e => handler(e.Value)) };
}
