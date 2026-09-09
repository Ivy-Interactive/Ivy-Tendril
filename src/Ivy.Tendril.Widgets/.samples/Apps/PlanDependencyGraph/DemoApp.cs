using Ivy;
using Ivy.Tendril.Widgets;
using PlanDependencyGraphWidget = Ivy.Tendril.Widgets.PlanDependencyGraph;

namespace WidgetSamples.Apps.PlanDependencyGraph;

[App(title: "Dependency Graph", icon: Icons.GitBranch, group: ["PlanDependencyGraph"])]
class DemoApp : ViewBase
{
    private static readonly PlanDependencyNodeDto[] Plans =
    [
        new("00041", "Plan schema and storage", "Completed", "Tendril"),
        new("00042", "Dependency checker", "Completed", "Tendril"),
        new("00043", "Job queue backoff", "Executing", "Tendril"),
        new("00044", "Auto retry on dependency merge", "Executing", "Tendril"),
        new("00045", "Dependency graph widget", "Review", "Tendril-Widgets"),
        new("00046", "Plan detail dependency tab", "Draft", "Tendril"),
        new("00047", "Blocked plan notifications", "Blocked", "Tendril"),
        new("00048", "Docs for plan dependencies", "Draft", "Tendril-Docs"),
        new("00049", "Legacy graph export", "Icebox", "Tendril"),
    ];

    private static readonly PlanDependencyEdgeDto[] Dependencies =
    [
        new("00042", "00041"),
        new("00043", "00041"),
        new("00044", "00042"),
        new("00044", "00043"),
        new("00045", "00042"),
        new("00046", "00045"),
        new("00047", "00044"),
        new("00047", "00045"),
        new("00048", "00046"),
    ];

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var selected = UseState<string?>(() => null);
        var horizontal = UseState(false);

        var graph = new PlanDependencyGraphWidget()
            .Nodes(Plans)
            .Edges(Dependencies)
            .SelectedId(selected.Value)
            .Orientation(horizontal.Value ? "horizontal" : "vertical")
            .OnNodeClick(id =>
            {
                selected.Set(id);
                client.Toast($"Plan {id} clicked", "OnNodeClick").Info();
            });

        var toolbar = Layout.Horizontal().Gap(2)
                      | new Button(horizontal.Value ? "Vertical" : "Horizontal",
                              () => horizontal.Set(!horizontal.Value))
                          .Variant(ButtonVariant.Secondary).Small()
                      | new Button("Clear selection", () => selected.Set((string?)null))
                          .Variant(ButtonVariant.Secondary).Small()
                      | Text.Muted(selected.Value is null ? "No plan selected" : $"Selected: {selected.Value}");

        return Layout.Vertical().Gap(2).Height(Size.Full())
               | toolbar
               | new Card(graph).Height(Size.Full());
    }
}

/// <summary>A plan.yaml can name a dependency that loops back; the widget draws it dashed.</summary>
[App(title: "Cyclic Graph", icon: Icons.GitBranch, group: ["PlanDependencyGraph"])]
class CyclicApp : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();

        var graph = new PlanDependencyGraphWidget()
            .Nodes(
                new PlanDependencyNodeDto("00061", "Extract shared client", "Executing"),
                new PlanDependencyNodeDto("00062", "Move auth into the client", "Draft"),
                new PlanDependencyNodeDto("00063", "Retire the old client", "Draft"))
            .Edges(
                new PlanDependencyEdgeDto("00062", "00061"),
                new PlanDependencyEdgeDto("00063", "00062"),
                new PlanDependencyEdgeDto("00061", "00063"))
            .ShowLegend(false)
            .OnNodeClick(id => client.Toast($"Plan {id} clicked", "OnNodeClick").Info());

        return new Card(graph).Height(Size.Full());
    }
}
