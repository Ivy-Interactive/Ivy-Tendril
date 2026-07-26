using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Widgets;
using Ivy.Tendril.Apps.Views;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;

namespace Ivy.Tendril.Apps.Workflows;

public class ContentView(
    IState<WorkflowItem?> selectedWorkflow,
    List<WorkflowItem> workflows,
    IPlanDatabaseService db,
    IJobService jobService,
    IClientProvider client,
    IState<bool> showChat,
    Action refreshWorkflows,
    IState<string?> projectFilter,
    Action triggerCreate) : ViewBase
{
    private static string? GetPromptwareFolderPath(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;

        // Try base directory
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Promptwares", provider);
        if (Directory.Exists(path)) return path;

        // Try source directory fallback for development
        path = Path.Combine("/Users/rorychatt/git/ivy/Ivy-Tendril/src/Ivy.Tendril/Promptwares", provider);
        if (Directory.Exists(path)) return path;

        return null;
    }

    private static WorkflowStep? FindStepInJson(string definitionJson, string stepId)
    {
        try
        {
            var def = JsonSerializer.Deserialize<WorkflowDefinition>(definitionJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return def?.Steps?.FirstOrDefault(s => s.Id == stepId);
        }
        catch
        {
            return null;
        }
    }

    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var isActiveState = UseState(selectedWorkflow.Value?.IsActive ?? false);
        var definitionState = UseState(selectedWorkflow.Value?.Definition ?? "{\"steps\":[]}");
        var selectedNodeId = UseState<string?>(null);
        var selectedInspectorFile = UseState<string?>("Program.md");
        var lastWorkflowId = UseState(selectedWorkflow.Value?.Id);

        UseEffect(() =>
        {
            if (selectedWorkflow.Value != null)
            {
                isActiveState.Set(selectedWorkflow.Value.IsActive);
                definitionState.Set(selectedWorkflow.Value.Definition);
                selectedNodeId.Set(null);
                selectedInspectorFile.Set("Program.md");
                lastWorkflowId.Set(selectedWorkflow.Value.Id);
            }
            return Disposable.Empty;
        }, selectedWorkflow);

        UseEffect(() =>
        {
            var currentWf = selectedWorkflow.Value;
            if (currentWf != null && lastWorkflowId.Value == currentWf.Id && isActiveState.Value != currentWf.IsActive)
            {
                var updated = currentWf with { IsActive = isActiveState.Value, Updated = DateTime.UtcNow };
                db.UpsertWorkflow(updated);
                selectedWorkflow.Set(updated);
                refreshWorkflows();
            }
            return Disposable.Empty;
        }, isActiveState);

        var wf = selectedWorkflow.Value;
        var connections = db.GetConnections();

        object? header = null;
        if (wf != null)
        {
            header = Layout.Horizontal()
                | (Layout.Vertical().AlignContent(Align.Left)
                   | Text.H2(wf.Name)
                  )
                | Layout.Horizontal().AlignContent(Align.Right)
                  | isActiveState.ToSwitchInput(label: "Active")
                  | new Button("Ask Assistant").Outline().OnClick(() => showChat.Set(!showChat.Value))
                  | new Button("Run").Outline().OnClick(() =>
                    {
                        var jobId = jobService.StartJob(new WorkflowRunArgs(wf.Id, "{}", wf.Project));
                        client.Toast($"Started run for workflow '{wf.Name}'. Job ID: {jobId}", "Started");
                    })
                  | (wf.IsSystem
                    ? new Button("Clone Workflow").Primary().OnClick(() =>
                      {
                          var name = wf.Name + " (Copy)";
                          var suffix = 1;
                          while (db.GetWorkflowByName(name, wf.Project) != null)
                          {
                              name = $"{wf.Name} (Copy {suffix++})";
                          }

                          var now = DateTime.UtcNow;
                          var newWf = wf with
                          {
                              Id = 0,
                              Name = name,
                              IsSystem = false,
                              Created = now,
                              Updated = now
                          };

                          db.UpsertWorkflow(newWf);

                          var created = db.GetWorkflowByName(name, wf.Project);
                          if (created != null)
                          {
                              selectedWorkflow.Set(created);
                          }

                          refreshWorkflows();
                          client.Toast($"Cloned workflow to '{name}'.", "Cloned");
                      })
                    : new Button("Save").Primary().OnClick(() =>
                      {
                          var updated = wf with { Definition = definitionState.Value, Updated = DateTime.UtcNow };
                          db.UpsertWorkflow(updated);
                          selectedWorkflow.Set(updated);
                          refreshWorkflows();
                          client.Toast("Workflow saved successfully.", "Saved");
                      }))
                  | (wf.IsSystem
                    ? null
                    : new Button("Delete").Destructive().OnClick(() =>
                      {
                          db.DeleteWorkflow(wf.Id);
                          selectedWorkflow.Set(null);
                          refreshWorkflows();
                          client.Toast($"Deleted workflow '{wf.Name}'.", "Deleted");
                      }));
        }

        // Render Promptware Inspector Panel if a prompt step is selected and pointing to a promptware
        object? inspectorPanel = null;
        if (selectedNodeId.Value != null)
        {
            var step = FindStepInJson(definitionState.Value, selectedNodeId.Value);
            if (step != null && step.Type.Equals("Prompt", StringComparison.OrdinalIgnoreCase))
            {
                var provider = step.Provider;
                var promptwareDir = GetPromptwareFolderPath(provider);
                if (promptwareDir != null)
                {
                    var toolsList = new List<string>();
                    var toolsDir = Path.Combine(promptwareDir, "Tools");
                    if (Directory.Exists(toolsDir))
                    {
                        toolsList.AddRange(Directory.GetFiles(toolsDir).Select(Path.GetFileName).Where(f => f != null)!);
                    }

                    var fileContent = "";
                    var selectedFile = selectedInspectorFile.Value ?? "Program.md";

                    if (selectedFile == "Program.md")
                    {
                        var filePath = Path.Combine(promptwareDir, "Program.md");
                        fileContent = File.Exists(filePath) ? File.ReadAllText(filePath) : "";
                    }
                    else
                    {
                        var filePath = Path.Combine(toolsDir, selectedFile);
                        fileContent = File.Exists(filePath) ? File.ReadAllText(filePath) : "";
                    }

                    var fileTabs = Layout.Horizontal();
                    
                    var programBtn = new Button("Program.md");
                    if (selectedFile == "Program.md") programBtn = programBtn.Primary();
                    else programBtn = programBtn.Outline();
                    fileTabs |= programBtn.OnClick(() => selectedInspectorFile.Set("Program.md"));

                    foreach (var tool in toolsList)
                    {
                        var toolBtn = new Button($"Tools/{tool}");
                        if (selectedFile == tool) toolBtn = toolBtn.Primary();
                        else toolBtn = toolBtn.Outline();
                        fileTabs |= toolBtn.OnClick(() => selectedInspectorFile.Set(tool));
                    }

                    var lang = Languages.Markdown;
                    if (selectedFile.EndsWith(".py")) lang = Languages.Python;
                    else if (selectedFile.EndsWith(".sh")) lang = Languages.Bash;
                    else if (selectedFile.EndsWith(".js") || selectedFile.EndsWith(".ts")) lang = Languages.Javascript;

                    inspectorPanel = new Box(
                        Layout.Vertical()
                        | (Layout.Horizontal()
                           | Text.Block($"Promptware Inspector: {provider}").Bold()
                          )
                        | fileTabs
                        | new CodeBlock(fileContent, lang)
                      );
                }
            }
        }

        var promptwaresDir = Path.Combine(configService.TendrilHome, "Promptwares");
        if (!Directory.Exists(promptwaresDir))
        {
            promptwaresDir = "/Users/rorychatt/git/ivy/Ivy-Tendril/src/Ivy.Tendril/Promptwares";
        }
        var systemPromptwaresList = Directory.Exists(promptwaresDir)
            ? Directory.GetDirectories(promptwaresDir)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(f => f != "memories" && f != "AgentChat" && f != "AGENTS.md" && f != ".DS_Store")
                .ToList()
            : new List<string>();

        var builderWidget = new WorkflowBuilder
        {
            WorkflowDefinitionJson = wf != null ? definitionState.Value : "",
            SelectedWorkflowId = wf?.Id ?? 0,
            AvailableConnections = connections.Select(c => new WorkflowConnectionInfo(c.Id, c.Name, c.Provider, c.Permissions)).ToList(),
            AvailableProviders = new List<string> { "Auto", "Review", "CreatePlan", "ExecutePlan", "CreatePr", "RetryPlan", "SetupProject", "CodeQuality", "CodeSecurity" },
            IsReadOnly = wf != null ? wf.IsSystem : true,
            SelectedNodeId = selectedNodeId.Value ?? "",
            Workflows = workflows.Select(w => new WorkflowSidebarItem(w.Id, w.Name, w.Description, w.Project, w.IsActive, w.IsSystem)).ToList(),
            SystemPromptwares = systemPromptwaresList,
            Projects = configService.Projects.Select(p => p.Name).ToList(),
            SelectedProject = projectFilter.Value ?? "default",
            OnSave = e =>
            {
                definitionState.Set(e.Value);
                return ValueTask.CompletedTask;
            },
            OnNodeSelect = e =>
            {
                selectedNodeId.Set(e.Value);
                selectedInspectorFile.Set("Program.md"); // reset file tab
                return ValueTask.CompletedTask;
            },
            OnProjectSelect = e =>
            {
                projectFilter.Set(e.Value);
                return ValueTask.CompletedTask;
            },
            OnWorkflowSelect = e =>
            {
                var selected = workflows.FirstOrDefault(w => w.Id == e.Value);
                if (selected != null)
                {
                    selectedWorkflow.Set(selected);
                }
                return ValueTask.CompletedTask;
            },
            OnCreateWorkflow = e =>
            {
                triggerCreate();
                return ValueTask.CompletedTask;
            }
        };

        var view = Layout.Vertical();
        if (header != null)
        {
            view |= header;
            view |= new Separator();
        }
        view |= builderWidget;
        if (inspectorPanel != null)
        {
            view |= inspectorPanel;
        }
        return view;
    }
}
