using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Widgets;
using Ivy.Tendril.Apps.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;

namespace Ivy.Tendril.Apps.Workflows;

public class ContentView(
    IState<WorkflowItem?> selectedWorkflow,
    List<WorkflowItem> workflows,
    IPlanDatabaseService db,
    IJobService jobService,
    IClientProvider client,
    IState<bool> showChat,
    Action refreshWorkflows) : ViewBase
{
    public override object Build()
    {
        var isActiveState = UseState(selectedWorkflow.Value?.IsActive ?? false);

        UseEffect(() =>
        {
            if (selectedWorkflow.Value != null)
            {
                isActiveState.Set(selectedWorkflow.Value.IsActive);
            }
            return Disposable.Empty;
        }, selectedWorkflow);

        UseEffect(() =>
        {
            var currentWf = selectedWorkflow.Value;
            if (currentWf != null && isActiveState.Value != currentWf.IsActive)
            {
                var updated = currentWf with { IsActive = isActiveState.Value, Updated = DateTime.UtcNow };
                db.UpsertWorkflow(updated);
                selectedWorkflow.Set(updated);
                refreshWorkflows();
            }
            return Disposable.Empty;
        }, isActiveState);

        if (selectedWorkflow.Value == null)
        {
            return new NoContentView(
                "No workflow selected",
                "Select a workflow from the sidebar or click 'Create Workflow' to start editing."
            );
        }

        var wf = selectedWorkflow.Value;
        var connections = db.GetConnections();

        var header = Layout.Horizontal()
            | (Layout.Vertical().AlignContent(Align.Left)
               | Text.H2(wf.Name)
              )
            | Layout.Horizontal().AlignContent(Align.Right)
              | isActiveState.ToSwitchInput(label: "Active")
              | new Button("Ask Assistant").Outline().OnClick(() => showChat.Set(!showChat.Value))
              | new Button("Run").Outline().OnClick(() =>
                {
                    var jobId = jobService.StartJob(new WorkflowRunArgs(wf.Id, "{}"));
                    client.Toast($"Started run for workflow '{wf.Name}'. Job ID: {jobId}", "Started");
                })
              | new Button("Delete").Destructive().OnClick(() =>
                {
                    db.DeleteWorkflow(wf.Id);
                    selectedWorkflow.Set(null);
                    refreshWorkflows();
                    client.Toast($"Deleted workflow '{wf.Name}'.", "Deleted");
                });

        return Layout.Vertical()
            | header
            | new Separator()
            | new WorkflowBuilder
            {
                WorkflowDefinitionJson = wf.Definition,
                AvailableConnections = connections.Select(c => new WorkflowConnectionInfo(c.Id, c.Name, c.Provider, c.Permissions)).ToList(),
                AvailableProviders = new List<string> { "Auto", "Review", "CreatePlan", "ExecutePlan" },
                OnSave = e =>
                {
                    var json = e.Value;
                    var updated = wf with { Definition = json, Updated = DateTime.UtcNow };
                    db.UpsertWorkflow(updated);
                    selectedWorkflow.Set(updated);
                    refreshWorkflows();
                    client.Toast("Workflow definition saved successfully.", "Saved");
                    return ValueTask.CompletedTask;
                }
            };
    }
}
