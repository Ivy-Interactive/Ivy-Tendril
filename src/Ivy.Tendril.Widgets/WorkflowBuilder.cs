using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ivy.Tendril.Widgets;

public record WorkflowSidebarItem(int Id, string Name, string Description, string Project, bool IsActive, bool IsSystem);

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
    [Prop] public string SelectedNodeId { get; init; } = "";
    [Prop] public bool IsReadOnly { get; init; } = false;
    [Prop] public int SelectedWorkflowId { get; init; } = 0;

    // Unified Sidebar Props
    [Prop] public List<WorkflowSidebarItem> Workflows { get; init; } = new();
    [Prop] public List<string> SystemPromptwares { get; init; } = new();
    [Prop] public List<string> Projects { get; init; } = new();
    [Prop] public string SelectedProject { get; init; } = "";

    [Event] public Func<Event<WorkflowBuilder, string>, ValueTask>? OnSave { get; init; }
    [Event] public Func<Event<WorkflowBuilder>, ValueTask>? OnDuplicate { get; init; }
    [Event] public Func<Event<WorkflowBuilder, string>, ValueTask>? OnNodeSelect { get; init; }
    
    // Unified Sidebar Events
    [Event] public Func<Event<WorkflowBuilder, int>, ValueTask>? OnWorkflowSelect { get; init; }
    [Event] public Func<Event<WorkflowBuilder, string>, ValueTask>? OnProjectSelect { get; init; }
    [Event] public Func<Event<WorkflowBuilder>, ValueTask>? OnCreateWorkflow { get; init; }
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

    public static WorkflowBuilder SelectedNodeId(this WorkflowBuilder w, string nodeId) =>
        w with { SelectedNodeId = nodeId };

    public static WorkflowBuilder OnNodeSelect(
        this WorkflowBuilder w,
        Func<Event<WorkflowBuilder, string>, ValueTask> handler
    ) => w with { OnNodeSelect = handler };

    public static WorkflowBuilder OnNodeSelect(this WorkflowBuilder w, Action<string> handler) =>
        w with
        {
            OnNodeSelect = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static WorkflowBuilder Workflows(this WorkflowBuilder w, List<WorkflowSidebarItem> workflows) =>
        w with { Workflows = workflows };

    public static WorkflowBuilder SystemPromptwares(this WorkflowBuilder w, List<string> promptwares) =>
        w with { SystemPromptwares = promptwares };

    public static WorkflowBuilder Projects(this WorkflowBuilder w, List<string> projects) =>
        w with { Projects = projects };

    public static WorkflowBuilder SelectedProject(this WorkflowBuilder w, string project) =>
        w with { SelectedProject = project };

    public static WorkflowBuilder SelectedWorkflowId(this WorkflowBuilder w, int id) =>
        w with { SelectedWorkflowId = id };

    public static WorkflowBuilder OnWorkflowSelect(
        this WorkflowBuilder w,
        Func<Event<WorkflowBuilder, int>, ValueTask> handler
    ) => w with { OnWorkflowSelect = handler };

    public static WorkflowBuilder OnWorkflowSelect(this WorkflowBuilder w, Action<int> handler) =>
        w with
        {
            OnWorkflowSelect = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static WorkflowBuilder OnProjectSelect(
        this WorkflowBuilder w,
        Func<Event<WorkflowBuilder, string>, ValueTask> handler
    ) => w with { OnProjectSelect = handler };

    public static WorkflowBuilder OnProjectSelect(this WorkflowBuilder w, Action<string> handler) =>
        w with
        {
            OnProjectSelect = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static WorkflowBuilder OnCreateWorkflow(
        this WorkflowBuilder w,
        Func<Event<WorkflowBuilder>, ValueTask> handler
    ) => w with { OnCreateWorkflow = handler };

    public static WorkflowBuilder OnCreateWorkflow(this WorkflowBuilder w, Action handler) =>
        w with
        {
            OnCreateWorkflow = e =>
            {
                handler();
                return ValueTask.CompletedTask;
            }
        };

    public static WorkflowBuilder IsReadOnly(this WorkflowBuilder w, bool readOnly) =>
        w with { IsReadOnly = readOnly };

    public static WorkflowBuilder OnDuplicate(
        this WorkflowBuilder w,
        Func<Event<WorkflowBuilder>, ValueTask> handler
    ) => w with { OnDuplicate = handler };

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
