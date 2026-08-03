using System;
using Ivy;
using Ivy.Core;

namespace Ivy.Tendril.Widgets;

public record BrainNode(
    string Id,
    string Label,
    string Type = "memory", // memory | file
    string Status = "clean", // clean | outdated | broken
    int LinkCount = 0
);

public record BrainEdge(
    string Source,
    string Target
);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "BrainMap",
    GlobalName = "IvyTendrilWidgets"
)]
public record BrainMap : WidgetBase<BrainMap>
{
    [Prop] public BrainNode[] Nodes { get; set; } = Array.Empty<BrainNode>();

    [Prop] public BrainEdge[] Edges { get; set; } = Array.Empty<BrainEdge>();

    [Prop] public string? SelectedNodeId { get; set; }

    [Event] public EventHandler<Event<BrainMap, string>>? OnNodeClick { get; set; }

    public static BrainMap operator |(BrainMap widget, object child)
    {
        throw new NotSupportedException("BrainMap does not support children.");
    }
}

public static class BrainMapExtensions
{
    public static BrainMap Nodes(this BrainMap map, params BrainNode[] nodes)
        => map with { Nodes = nodes };

    public static BrainMap Nodes(this BrainMap map, System.Collections.Generic.IEnumerable<BrainNode> nodes)
        => map with { Nodes = System.Linq.Enumerable.ToArray(nodes) };

    public static BrainMap Edges(this BrainMap map, params BrainEdge[] edges)
        => map with { Edges = edges };

    public static BrainMap Edges(this BrainMap map, System.Collections.Generic.IEnumerable<BrainEdge> edges)
        => map with { Edges = System.Linq.Enumerable.ToArray(edges) };

    public static BrainMap SelectedNodeId(this BrainMap map, string? selectedNodeId)
        => map with { SelectedNodeId = selectedNodeId };

    public static BrainMap OnNodeClick(this BrainMap map, Action<string> onNodeClick)
        => map with { OnNodeClick = new(evt => { onNodeClick(evt.Value); return System.Threading.Tasks.ValueTask.CompletedTask; }) };
}
