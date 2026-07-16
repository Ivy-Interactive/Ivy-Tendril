using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ivy.Tendril.Widgets;

public record WorkflowConnectionInfo(int Id, string Name, string Provider, string Permissions);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "WorkflowBuilder",
    GlobalName = "IvyTendrilWidgets"
)]
public record WorkflowBuilder : WidgetBase<WorkflowBuilder>
{
    [Prop] public string WorkflowDefinitionJson { get; init; } = "";
    [Prop] public List<WorkflowConnectionInfo> AvailableConnections { get; init; } = new();
    [Prop] public List<string> AvailableProviders { get; init; } = new();

    [Event] public Func<Event<WorkflowBuilder, string>, ValueTask>? OnSave { get; init; }
}

public static class WorkflowBuilderExtensions
{
    public static WorkflowBuilder WorkflowDefinitionJson(this WorkflowBuilder w, string json) =>
        w with { WorkflowDefinitionJson = json };

    public static WorkflowBuilder AvailableConnections(this WorkflowBuilder w, List<WorkflowConnectionInfo> connections) =>
        w with { AvailableConnections = connections };

    public static WorkflowBuilder AvailableProviders(this WorkflowBuilder w, List<string> providers) =>
        w with { AvailableProviders = providers };

    public static WorkflowBuilder OnSave(
        this WorkflowBuilder w,
        Func<Event<WorkflowBuilder, string>, ValueTask> handler
    ) => w with { OnSave = handler };

    public static WorkflowBuilder OnSave(this WorkflowBuilder w, Action<string> handler) =>
        w with
        {
            OnSave = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static WorkflowBuilder Bind(this WorkflowBuilder w, IState<string> state) =>
        w with
        {
            WorkflowDefinitionJson = state.Value,
            OnSave = e =>
            {
                state.Set(e.Value);
                return ValueTask.CompletedTask;
            }
        };
}
