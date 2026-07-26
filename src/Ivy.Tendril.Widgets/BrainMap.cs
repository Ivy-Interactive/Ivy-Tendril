using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;

namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "BrainMap",
    GlobalName = "IvyTendrilWidgets"
)]
public record BrainMap : WidgetBase<BrainMap>
{
    public BrainMap() : base()
    {
    }

    [Prop] public List<BrainNode> Nodes { get; init; } = [];
    [Prop] public List<BrainEdge> Edges { get; init; } = [];
    [Prop] public string? SelectedNodeId { get; init; }

    [Event] public EventHandler<Event<BrainMap, string>>? OnNodeClick { get; init; }
}

public record BrainNode(string Id, string Label, string Type, string Status);
public record BrainEdge(string Source, string Target);

public static class BrainMapExtensions
{
    public static BrainMap Nodes(this BrainMap w, IEnumerable<BrainNode> nodes) =>
        w with { Nodes = nodes.ToList() };

    public static BrainMap Edges(this BrainMap w, IEnumerable<BrainEdge> edges) =>
        w with { Edges = edges.ToList() };

    public static BrainMap SelectedNodeId(this BrainMap w, string? selectedNodeId) =>
        w with { SelectedNodeId = selectedNodeId };

    public static BrainMap OnNodeClick(this BrainMap w, Func<Event<BrainMap, string>, ValueTask> handler) =>
        w with { OnNodeClick = new(handler) };

    public static BrainMap OnNodeClick(this BrainMap w, Action<string> handler) =>
        w with
        {
            OnNodeClick = new(e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            })
        };
}
