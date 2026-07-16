using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

// --- Settings ---

public class WorkflowAddSettings : CommandSettings
{
    [Description("Unique name for this workflow")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [CommandOption("-d|--desc <value>")]
    [Description("Description of the workflow")]
    public string Description { get; set; } = "";

    [CommandOption("-c|--definition <value>")]
    [Description("Raw workflow definition JSON string")]
    public string Definition { get; set; } = "";

    [CommandOption("-f|--file <path>")]
    [Description("File containing workflow definition JSON")]
    public string? FilePath { get; set; }

    [CommandOption("--stdin")]
    [Description("Read workflow definition JSON from standard input")]
    public bool Stdin { get; set; }

    [CommandOption("--project <value>")]
    [Description("Project name (defaults to current folder's project)")]
    public string? Project { get; set; }
}

public class WorkflowRemoveSettings : CommandSettings
{
    [Description("Name or ID of the workflow to remove")]
    [CommandArgument(0, "<name-or-id>")]
    public string NameOrId { get; set; } = "";

    [CommandOption("--project <value>")]
    [Description("Project name (defaults to current folder's project)")]
    public string? Project { get; set; }
}

public class WorkflowRunSettings : CommandSettings
{
    [Description("Name or ID of the workflow to run")]
    [CommandArgument(0, "<name-or-id>")]
    public string NameOrId { get; set; } = "";

    [CommandOption("-p|--payload <value>")]
    [Description("JSON trigger payload")]
    public string Payload { get; set; } = "";

    [CommandOption("-f|--file <path>")]
    [Description("File containing JSON trigger payload")]
    public string? FilePath { get; set; }

    [CommandOption("--stdin")]
    [Description("Read JSON trigger payload from standard input")]
    public bool Stdin { get; set; }

    [CommandOption("--project <value>")]
    [Description("Project name (defaults to current folder's project)")]
    public string? Project { get; set; }
}

public class WorkflowListSettings : CommandSettings
{
    [CommandOption("--project <value>")]
    [Description("Filter workflows by project")]
    public string? Project { get; set; }
}

// --- Commands ---

public class WorkflowListCommand(IPlanDatabaseService db, IConfigService configService) : Command<WorkflowListSettings>
{
    private readonly IPlanDatabaseService _db = db;
    private readonly IConfigService _configService = configService;

    protected override int Execute(CommandContext context, WorkflowListSettings settings, System.Threading.CancellationToken cancellationToken)
    {
        var project = settings.Project;
        if (string.IsNullOrEmpty(project))
        {
            var currentDir = System.IO.Directory.GetCurrentDirectory();
            project = PromptwareHelper.FindProjectNameForPath(currentDir, _configService.TendrilHome);
        }

        var workflows = _db.GetWorkflows(project);
        if (workflows.Count == 0)
        {
            AnsiConsole.MarkupLine(string.IsNullOrEmpty(project) 
                ? "[yellow]No workflows defined.[/]" 
                : $"[yellow]No workflows defined for project '{project}'.[/]");
            return 0;
        }

        var table = new Spectre.Console.Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]ID[/]");
        table.AddColumn("[bold]Name[/]");
        table.AddColumn("[bold]Project[/]");
        table.AddColumn("[bold]Description[/]");
        table.AddColumn("[bold]Active[/]");
        table.AddColumn("[bold]Created[/]");
        table.AddColumn("[bold]Updated[/]");

        foreach (var wf in workflows)
        {
            table.AddRow(
                wf.Id.ToString(),
                wf.Name,
                wf.Project,
                wf.Description ?? "",
                wf.IsActive ? "[green]Yes[/]" : "[red]No[/]",
                wf.Created.ToString("g"),
                wf.Updated.ToString("g")
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

public class WorkflowAddCommand(IPlanDatabaseService db, IConfigService configService) : Command<WorkflowAddSettings>
{
    private readonly IPlanDatabaseService _db = db;
    private readonly IConfigService _configService = configService;

    protected override int Execute(CommandContext context, WorkflowAddSettings settings, System.Threading.CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            AnsiConsole.MarkupLine("[red]Error: Workflow name cannot be empty.[/]");
            return 1;
        }

        var definitionJson = Ivy.Tendril.Helpers.ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, settings.Definition);
        if (string.IsNullOrWhiteSpace(definitionJson))
        {
            AnsiConsole.MarkupLine("[red]Error: Workflow definition JSON cannot be empty.[/]");
            return 1;
        }

        // Validate JSON structure
        try
        {
            using var doc = JsonDocument.Parse(definitionJson);
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: Invalid workflow definition JSON: {ex.Message}[/]");
            return 1;
        }

        var project = settings.Project;
        if (string.IsNullOrEmpty(project))
        {
            var currentDir = System.IO.Directory.GetCurrentDirectory();
            project = PromptwareHelper.FindProjectNameForPath(currentDir, _configService.TendrilHome) ?? "default";
        }

        var existing = _db.GetWorkflowByName(settings.Name, project);
        var workflow = new WorkflowItem
        {
            Id = existing?.Id ?? 0,
            Name = settings.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(settings.Description) ? (existing?.Description ?? "") : settings.Description.Trim(),
            Project = project,
            Definition = definitionJson.Trim(),
            IsActive = true,
            Created = existing?.Created ?? DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };

        _db.UpsertWorkflow(workflow);
        AnsiConsole.MarkupLine($"[green]Successfully saved workflow '{workflow.Name}' for project '{project}'.[/]");
        return 0;
    }
}

public class WorkflowRemoveCommand(IPlanDatabaseService db, IConfigService configService) : Command<WorkflowRemoveSettings>
{
    private readonly IPlanDatabaseService _db = db;
    private readonly IConfigService _configService = configService;

    protected override int Execute(CommandContext context, WorkflowRemoveSettings settings, System.Threading.CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.NameOrId))
        {
            AnsiConsole.MarkupLine("[red]Error: Please specify a workflow name or ID.[/]");
            return 1;
        }

        var project = settings.Project;
        if (string.IsNullOrEmpty(project))
        {
            var currentDir = System.IO.Directory.GetCurrentDirectory();
            project = PromptwareHelper.FindProjectNameForPath(currentDir, _configService.TendrilHome);
        }

        WorkflowItem? workflow = null;
        if (int.TryParse(settings.NameOrId, out var id))
        {
            workflow = _db.GetWorkflowById(id);
        }
        if (workflow == null)
        {
            workflow = _db.GetWorkflowByName(settings.NameOrId, project);
        }

        if (workflow == null)
        {
            AnsiConsole.MarkupLine(string.IsNullOrEmpty(project)
                ? $"[red]Error: Workflow '{settings.NameOrId}' not found.[/]"
                : $"[red]Error: Workflow '{settings.NameOrId}' not found in project '{project}'.[/]");
            return 1;
        }

        _db.DeleteWorkflow(workflow.Id);
        AnsiConsole.MarkupLine($"[green]Successfully deleted workflow '{workflow.Name}' (ID {workflow.Id}) from project '{workflow.Project}'.[/]");
        return 0;
    }
}

public class WorkflowRunCommand(IPlanDatabaseService db, IJobService jobService, IConfigService configService) : Command<WorkflowRunSettings>
{
    private readonly IPlanDatabaseService _db = db;
    private readonly IJobService _jobService = jobService;
    private readonly IConfigService _configService = configService;

    protected override int Execute(CommandContext context, WorkflowRunSettings settings, System.Threading.CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.NameOrId))
        {
            AnsiConsole.MarkupLine("[red]Error: Please specify a workflow name or ID to run.[/]");
            return 1;
        }

        var project = settings.Project;
        if (string.IsNullOrEmpty(project))
        {
            var currentDir = System.IO.Directory.GetCurrentDirectory();
            project = PromptwareHelper.FindProjectNameForPath(currentDir, _configService.TendrilHome);
        }

        WorkflowItem? workflow = null;
        if (int.TryParse(settings.NameOrId, out var id))
        {
            workflow = _db.GetWorkflowById(id);
        }
        if (workflow == null)
        {
            workflow = _db.GetWorkflowByName(settings.NameOrId, project);
        }

        if (workflow == null)
        {
            AnsiConsole.MarkupLine(string.IsNullOrEmpty(project)
                ? $"[red]Error: Workflow '{settings.NameOrId}' not found.[/]"
                : $"[red]Error: Workflow '{settings.NameOrId}' not found in project '{project}'.[/]");
            return 1;
        }

        var payloadJson = Ivy.Tendril.Helpers.ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, settings.Payload);
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            payloadJson = "{}";
        }

        // Validate JSON payload
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: Invalid trigger payload JSON: {ex.Message}[/]");
            return 1;
        }

        var args = new WorkflowRunArgs(workflow.Id, payloadJson);
        var jobId = _jobService.StartJob(args);

        AnsiConsole.MarkupLine($"[green]Started workflow run job. Job ID:[/] [bold]{jobId}[/]");
        return 0;
    }
}
