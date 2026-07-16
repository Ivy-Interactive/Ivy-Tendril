using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Apps.Workflows;
using Ivy.Tendril.Apps.Agent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;

namespace Ivy.Tendril.Apps;

[App(title: "Workflows", icon: Icons.Workflow, group: ["Automations"], order: 22)]
public class WorkflowsApp : ViewBase
{
    private static void SeedDefaultWorkflowsForProject(IPlanDatabaseService db, string project)
    {
        var now = DateTime.UtcNow;
        db.UpsertWorkflow(new WorkflowItem
        {
            Name = "Code Quality Audit",
            Description = "Analyze the codebase changes and run quality checks to identify formatting, complexity, or redundancy issues.",
            Project = project,
            Definition = "{\n  \"steps\": [\n    {\n      \"id\": \"start\",\n      \"name\": \"Start\",\n      \"type\": \"Trigger\",\n      \"connectionName\": \"\",\n      \"action\": \"\",\n      \"args\": \"{}\",\n      \"next\": [\"checkquality\"]\n    },\n    {\n      \"id\": \"checkquality\",\n      \"name\": \"CheckQuality\",\n      \"type\": \"Prompt\",\n      \"connectionName\": \"\",\n      \"action\": \"\",\n      \"args\": \"Analyze the codebase for formatting violations, complex logic, anti-patterns, resource leaks, or missing unit tests.\",\n      \"provider\": \"CodeQuality\",\n      \"model\": \"default\",\n      \"next\": []\n    }\n  ]\n}",
            IsActive = true,
            Created = now,
            Updated = now
        });

        db.UpsertWorkflow(new WorkflowItem
        {
            Name = "Code Security Scan",
            Description = "Run a security scan to find credentials leak, insecure dependencies, or vulnerabilities.",
            Project = project,
            Definition = "{\n  \"steps\": [\n    {\n      \"id\": \"start\",\n      \"name\": \"Start\",\n      \"type\": \"Trigger\",\n      \"connectionName\": \"\",\n      \"action\": \"\",\n      \"args\": \"{}\",\n      \"next\": [\"scansecurity\"]\n    },\n    {\n      \"id\": \"scansecurity\",\n      \"name\": \"ScanSecurity\",\n      \"type\": \"Prompt\",\n      \"connectionName\": \"\",\n      \"action\": \"\",\n      \"args\": \"Inspect the codebase for hardcoded secrets, insecure package dependencies, SQL injection/XSS risks, and weak crypto configurations.\",\n      \"provider\": \"CodeSecurity\",\n      \"model\": \"default\",\n      \"next\": []\n    }\n  ]\n}",
            IsActive = true,
            Created = now,
            Updated = now
        });
    }

    public override object Build()
    {
        var db = UseService<IPlanDatabaseService>();
        var jobService = UseService<IJobService>();
        var client = UseService<IClientProvider>();
        var configService = UseService<IConfigService>();

        var selectedWorkflow = UseState<WorkflowItem?>(null);
        var textFilter = UseState("");
        var projectFilter = UseState<string?>(null);
        var refreshToken = UseRefreshToken();

        var showCreateDialog = UseState(false);
        var nameText = UseState("");
        var descText = UseState("");

        var showChat = UseState(false);
        var chatSessionId = UseState(() => Guid.NewGuid().ToString());

        // Default to first project if not set
        if (projectFilter.Value == null && configService.Projects.Count > 0)
        {
            projectFilter.Set(configService.Projects[0].Name);
        }

        var currentProject = projectFilter.Value;
        if (!string.IsNullOrEmpty(currentProject) && db.GetWorkflows(currentProject).Count == 0)
        {
            SeedDefaultWorkflowsForProject(db, currentProject);
        }

        var workflows = db.GetWorkflows(currentProject);

        // When project changes, auto-select first workflow of that project
        UseEffect(() =>
        {
            var pWorkflows = db.GetWorkflows(projectFilter.Value);
            selectedWorkflow.Set(pWorkflows.FirstOrDefault());
            return Disposable.Empty;
        }, projectFilter);

        // Create Workflow Dialog
        object? createDialog = null;
        if (showCreateDialog.Value)
        {
            createDialog = new Dialog(
                _ => showCreateDialog.Set(false),
                new DialogHeader("Create Workflow"),
                new DialogBody(
                    Layout.Vertical()
                    | Text.P($"Create a background automation workflow for project '{projectFilter.Value ?? "default"}'.")
                    | nameText.ToTextInput("Workflow name (e.g. SecurityAudit)").AutoFocus()
                    | descText.ToTextInput("Short description of what it does...")
                ),
                new DialogFooter(
                    new Button("Cancel").Outline().OnClick(() => showCreateDialog.Set(false)),
                    new Button("Create").Primary().ShortcutKey("Enter").OnClick(() =>
                    {
                        var name = nameText.Value.Trim();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            client.Toast("Workflow name cannot be empty.", "Error");
                            return;
                        }

                        var project = projectFilter.Value ?? "default";
                        if (db.GetWorkflowByName(name, project) != null)
                        {
                            client.Toast($"A workflow with name '{name}' already exists in project '{project}'.", "Error");
                            return;
                        }

                        var newWf = new WorkflowItem
                        {
                            Name = name,
                            Description = descText.Value.Trim(),
                            Project = project,
                            Definition = "{\n  \"steps\": [\n    {\n      \"id\": \"start\",\n      \"name\": \"Start\",\n      \"type\": \"Trigger\",\n      \"connectionName\": \"\",\n      \"action\": \"\",\n      \"args\": \"{}\",\n      \"next\": [],\n      \"x\": 50,\n      \"y\": 200\n    }\n  ]\n}",
                            IsActive = true,
                            Created = DateTime.UtcNow,
                            Updated = DateTime.UtcNow
                        };

                        db.UpsertWorkflow(newWf);
                        
                        // Reset form
                        nameText.Set("");
                        descText.Set("");
                        showCreateDialog.Set(false);

                        // Reload list and open editor
                        refreshToken.Refresh();
                        
                        var created = db.GetWorkflowByName(name, project);
                        if (created != null)
                        {
                            selectedWorkflow.Set(created);
                        }
                        
                        client.Toast($"Created workflow '{name}' successfully.", "Created");
                    })
                )
            );
        }

        var sidebar = new SidebarView(workflows, selectedWorkflow, projectFilter, textFilter, configService, () => showCreateDialog.Set(true));
        var content = new ContentView(selectedWorkflow, workflows, db, jobService, client, showChat, () => refreshToken.Refresh());

        var mainLayout = new SidebarLayout(
            content,
            sidebar
        ).SidebarContentScroll(Scroll.None);

        object mainView = mainLayout;

        if (showChat.Value)
        {
            mainView = Layout.Horizontal()
                | mainLayout
                | new Box(new AgentApp.AgentChatView(chatSessionId.Value)).Width(Size.Px(380));
        }

        return new Fragment(mainView, createDialog);
    }
}
