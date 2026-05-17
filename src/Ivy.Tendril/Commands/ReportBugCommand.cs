using System.ComponentModel;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly Regex JobIdFromLogRegex = new(@"^(\d{5})-", RegexOptions.Compiled);
    private const string BugReportApiUrl = "https://tendril-api.ivy.app/report-bug";

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
            _logger.LogError(ex, "Failed to report bug");
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }
    }

    private int Run(ReportBugSettings settings, CancellationToken cancellationToken)
    {
        var configService = new ConfigService();
        var description = settings.Description;
        if (string.IsNullOrEmpty(description))
            description = AnsiConsole.Ask<string>("Describe the bug:");

        var files = CollectFiles(settings, configService);
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

        var zipPath = CreateZip(files);
        try
        {
            var version = typeof(ReportBugCommand).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            var osVersion = Environment.OSVersion.VersionString;
            var agent = configService.Settings.CodingAgent;

            var result = UploadReport(zipPath, description, osVersion, version, agent, cancellationToken);
            if (result == null) return 1;

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]Bug report submitted successfully![/]");
            AnsiConsole.MarkupLine($"[link]{result.IssueUrl}[/]");
            return 0;
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    private List<BugReportFile> CollectFiles(ReportBugSettings settings, ConfigService configService)
    {
        var files = new List<BugReportFile>();
        var jobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(settings.PlanId))
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            CollectPlanFiles(planFolder, files);
            ExtractJobIdsFromPlanLogs(planFolder, jobIds);
        }

        if (!string.IsNullOrEmpty(settings.JobId))
        {
            var normalized = NormalizeJobId(settings.JobId);
            jobIds.Add(normalized);
        }

        if (jobIds.Count > 0)
            CollectPromptwareLogFiles(jobIds, configService, files);

        return files;
    }

    private static void CollectPlanFiles(string planFolder, List<BugReportFile> files)
    {
        foreach (var file in Directory.EnumerateFiles(planFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(planFolder, file);
            if (relativePath.StartsWith("Worktrees", StringComparison.OrdinalIgnoreCase))
                continue;

            files.Add(new BugReportFile(file, relativePath));
        }
    }

    private static void ExtractJobIdsFromPlanLogs(string planFolder, HashSet<string> jobIds)
    {
        var logsDir = Path.Combine(planFolder, "Logs");
        if (!Directory.Exists(logsDir)) return;

        foreach (var logFile in Directory.GetFiles(logsDir, "*.md"))
        {
            var fileName = Path.GetFileNameWithoutExtension(logFile);
            var match = JobIdFromLogRegex.Match(fileName);
            if (match.Success)
                jobIds.Add(match.Groups[1].Value);
        }
    }

    private static void CollectPromptwareLogFiles(HashSet<string> jobIds, ConfigService configService, List<BugReportFile> files)
    {
        var promptsRoot = PromptwareHelper.ResolvePromptsRoot(configService.TendrilHome);
        if (!Directory.Exists(promptsRoot)) return;

        foreach (var pwDir in Directory.GetDirectories(promptsRoot))
        {
            var logsDir = Path.Combine(pwDir, "Logs");
            if (!Directory.Exists(logsDir)) continue;

            var pwName = Path.GetFileName(pwDir);

            foreach (var jobId in jobIds)
            {
                var mdFile = Path.Combine(logsDir, $"{jobId}.md");
                if (File.Exists(mdFile))
                    files.Add(new BugReportFile(mdFile, Path.Combine("Jobs", pwName, $"{jobId}.md")));

                var jsonlFile = Path.Combine(logsDir, $"{jobId}.raw.jsonl");
                if (File.Exists(jsonlFile))
                    files.Add(new BugReportFile(jsonlFile, Path.Combine("Jobs", pwName, $"{jobId}.raw.jsonl")));
            }
        }
    }

    private static string NormalizeJobId(string input)
    {
        input = input.Trim();
        if (int.TryParse(input, out var num))
            return num.ToString("D5");
        return input;
    }

    private static void DisplayFileList(List<BugReportFile> files)
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

    private static string CreateZip(List<BugReportFile> files)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"tendril-bug-report-{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        foreach (var file in files)
        {
            zip.CreateEntryFromFile(file.AbsolutePath, file.ZipEntryPath.Replace('\\', '/'));
        }

        return zipPath;
    }

    private BugReportResult? UploadReport(string zipPath, string description, string osVersion, string tendrilVersion, string agent, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(description), "description");
        form.Add(new StringContent(osVersion), "osVersion");
        form.Add(new StringContent(tendrilVersion), "tendrilVersion");
        form.Add(new StringContent(agent), "agent");

        var fileStream = File.OpenRead(zipPath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(fileContent, "file", "bug-report.zip");

        AnsiConsole.MarkupLine("[dim]Uploading bug report...[/]");

        var response = httpClient.PostAsync(BugReportApiUrl, form, cancellationToken).GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            AnsiConsole.MarkupLine($"[red]Upload failed ({(int)response.StatusCode}):[/] {body.EscapeMarkup()}");
            return null;
        }

        var json = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<BugReportResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private record BugReportFile(string AbsolutePath, string ZipEntryPath);

    private record BugReportResult(string ReportId, string IssueUrl);
}
