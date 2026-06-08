using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class ReportBugSettings : CommandSettings
{
    [CommandOption("--plan")]
    [Description("Plan ID to include in the report")]
    public string? PlanId { get; set; }

    [CommandOption("--job")]
    [Description("Job ID to include in the report")]
    public string? JobId { get; set; }

    [CommandOption("--description|-d")]
    [Description("Bug description")]
    public string? Description { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }

    [CommandOption("--dry-run")]
    [Description("Show what would be sent without uploading")]
    public bool DryRun { get; set; }
}

public class ReportBugCommand : Command<ReportBugSettings>
{
    private readonly ILogger<ReportBugCommand> _logger;

    public ReportBugCommand(ILogger<ReportBugCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ReportBugSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.PlanId) && string.IsNullOrEmpty(settings.JobId))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Either --plan or --job must be specified.");
            return 1;
        }

        try
        {
            return Run(settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to report bug: {Message}", ex.Message);
            return 1;
        }
    }

    private int Run(ReportBugSettings settings, CancellationToken cancellationToken)
    {
        var configService = new ConfigService();
        var service = new BugReportService(configService);

        var description = settings.Description;
        if (string.IsNullOrEmpty(description))
            description = AnsiConsole.Ask<string>("Describe the bug:");

        var files = CollectFiles(settings, service);
        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No files found to include in the report.[/]");
            return 1;
        }

        DisplayFileList(files);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Warning:[/] These files will be attached to a public GitHub issue.");
        AnsiConsole.MarkupLine("[yellow]If this project contains sensitive data, consider reporting via another channel.[/]");
        AnsiConsole.WriteLine();

        if (!settings.Yes && !AnsiConsole.Confirm("Proceed with bug report?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
            return 0;
        }

        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine("[dim]Dry run — no report submitted.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[dim]Uploading bug report...[/]");
        var result = service.SubmitReportAsync(description, files, cancellationToken).GetAwaiter().GetResult();

        if (result == null)
        {
            AnsiConsole.MarkupLine("[red]Upload failed.[/]");
            return 1;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Bug report submitted successfully![/]");
        AnsiConsole.MarkupLine($"[link]{result.IssueUrl}[/]");
        return 0;
    }

    private static List<BugReportService.BugReportFile> CollectFiles(ReportBugSettings settings, BugReportService service)
    {
        var files = new List<BugReportService.BugReportFile>();

        if (!string.IsNullOrEmpty(settings.PlanId))
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            files.AddRange(service.CollectFilesForPlan(planFolder));
        }

        if (!string.IsNullOrEmpty(settings.JobId))
            files.AddRange(service.CollectFilesForJob(settings.JobId));

        return files;
    }

    private static void DisplayFileList(List<BugReportService.BugReportFile> files)
    {
        var table = new Spectre.Console.Table();
        table.AddColumn("File");
        table.AddColumn(new Spectre.Console.TableColumn("Size").RightAligned());

        long totalSize = 0;
        foreach (var file in files)
        {
            var size = new FileInfo(file.AbsolutePath).Length;
            totalSize += size;
            table.AddRow(
                file.ZipEntryPath.EscapeMarkup(),
                FormatSize(size));
        }

        table.AddEmptyRow();
        table.AddRow("[bold]Total[/]", $"[bold]{FormatSize(totalSize)}[/]");

        AnsiConsole.Write(table);
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
        };
    }
}
