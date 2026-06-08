using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy.Tendril.Agents;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs;

public class JobDebugSheet(
    string jobId,
    IJobService jobService,
    IPlanReaderService planService,
    IConfigService config) : ViewBase
{
    public override object Build()
    {
        var copyToClipboard = UseClipboard();
        var client = UseService<IClientProvider>();
        var showReportDialog = UseState(false);

        var job = jobService.GetJob(jobId);
        if (job is null)
            return Text.P("Job not found.");

        var data = new
        {
            JobId = job.Id,
            PlanId = GetPlanId(job),
            PromptTitle = JobsApp.GetFullPrompt(job, planService) ?? "",
            Status = $"{job.Status}{(job.StatusMessage != null ? $": {job.StatusMessage}" : "")}",
            job.Type,
            job.Project,
            job.Provider,
            Model = job.Model ?? "",
            SessionId = job.SessionId ?? "",
            Started = job.StartedAt?.ToString("u") ?? "",
            Completed = job.CompletedAt?.ToString("u") ?? "",
            Duration = job.DurationSeconds.HasValue ? $"{job.DurationSeconds}s" : "",
            Cost = job.Cost.HasValue ? $"${job.Cost:F4}" : "",
            Tokens = job.Tokens.HasValue ? $"{job.Tokens:N0}" : "",
            PermissionDenials = FormatPermissionDenials(job),
            PlanFolder = GetPlanFolderPath(job) ?? "",
            PlanLog = GetPlanLogPath(job) ?? "",
            PlanCliLog = GetPlanCliLogPath(job) ?? "",
            PromptwareLearnings = GetPromptwareLearnings(job),
            PromptwareLog = GetPromptwareLogPath(job) ?? "",
            PromptwareRawLog = GetPromptwareRawLogPath(job) ?? "",
            ExitCode = job.ExitCode?.ToString() ?? "",
            WorkingDirectory = job.WorkingDirectory ?? "",
            CliCommand = job.CliCommand ?? ""
        };

        var detailsView = data.ToDetails()
            .Multiline(x => x.PromptTitle)
            .Multiline(x => x.PermissionDenials)
            .Multiline(x => x.PromptwareLearnings)
            .Multiline(x => x.Status)
            .Label(x => x.PromptTitle, "Prompt/Title")
            .Label(x => x.PlanId, "Plan Id")
            .Label(x => x.SessionId, "Session Id")
            .Label(x => x.PlanFolder, "Plan Folder")
            .Label(x => x.PlanLog, "Plan Log")
            .Label(x => x.PlanCliLog, "Plan CLI Log")
            .Label(x => x.PromptwareLearnings, "Promptware Learnings")
            .Label(x => x.PromptwareLog, "Promptware Log")
            .Label(x => x.PromptwareRawLog, "Promptware Raw Log")
            .Label(x => x.PermissionDenials, "Permission Denials")
            .Label(x => x.ExitCode, "Exit Code")
            .Label(x => x.WorkingDirectory, "Working Directory")
            .Label(x => x.CliCommand, "Arguments")
            .Label(x => x.JobId, "Job Id")
            .Builder(x => x.PermissionDenials, f => f.Func((string denials) =>
                new CodeBlock(denials)))
            .Builder(x => x.PromptwareLearnings, f => f.Func((string assets) =>
                new CodeBlock(assets)))
            .Builder(x => x.PlanFolder, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.PlanLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.PlanCliLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.PromptwareLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.PromptwareRawLog,
                f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.WorkingDirectory, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.CliCommand, f => f.Func((string cmd) => new CodeBlock(cmd)))
            .Builder(x => x.JobId, f => f.CopyToClipboard())
            .Builder(x => x.PlanId, f => f.CopyToClipboard());

        var header = Layout.Horizontal().Gap(2)
            | new Button("Copy Details").Icon(Icons.ClipboardCopy).Outline().OnClick(() =>
            {
                var lines = new List<(string Label, string Value)>
                {
                    ("Job Id", data.JobId),
                    ("Plan Id", data.PlanId),
                    ("Prompt/Title", data.PromptTitle),
                    ("Status", data.Status),
                    ("Type", data.Type),
                    ("Project", data.Project),
                    ("Provider", data.Provider),
                    ("Model", data.Model),
                    ("Session Id", data.SessionId),
                    ("Started", data.Started),
                    ("Completed", data.Completed),
                    ("Duration", data.Duration),
                    ("Cost", data.Cost),
                    ("Tokens", data.Tokens),
                    ("Exit Code", data.ExitCode),
                    ("Working Directory", data.WorkingDirectory),
                    ("Arguments", data.CliCommand),
                    ("Permission Denials", data.PermissionDenials),
                    ("Plan Folder", data.PlanFolder),
                    ("Plan Log", data.PlanLog),
                    ("Plan CLI Log", data.PlanCliLog),
                    ("Promptware Learnings", data.PromptwareLearnings),
                    ("Promptware Log", data.PromptwareLog),
                    ("Promptware Raw Log", data.PromptwareRawLog),
                };

                var formatted = string.Join("\n", lines
                    .Where(l => !string.IsNullOrEmpty(l.Value))
                    .Select(l => $"{l.Label}: {l.Value}"));

                copyToClipboard(formatted);
                client.Toast("Job details copied to clipboard", "Copied");
            })
            | new Button("Report Bug").Icon(Icons.Bug).OnClick(() => showReportDialog.Set(true));

        return Layout.Vertical()
            | new HeaderLayout(header, detailsView)
            | (showReportDialog.Value ? new ReportBugDialog(showReportDialog, jobId) : null);
    }

    private object PathDropDown(string path, Action<string> copyToClipboard, IClientProvider client)
    {
        return Layout.Horizontal().Gap(2).AlignContent(Align.Center)
            | Text.Block(path)
            | new Button().Icon(Icons.EllipsisVertical).Ghost().Small()
                .WithDropDown(
                    new MenuItem("Copy to Clipboard", Icon: Icons.ClipboardCopy, Tag: "Copy")
                        .OnSelect(() => copyToClipboard(path)),
                    new MenuItem($"Open in {config.Editor.Label}", Icon: Icons.Code, Tag: "OpenInEditor")
                        .OnSelect(() =>
                        {
                            try
                            {
                                config.OpenInEditor(path);
                            }
                            catch (EditorNotAvailableException ex)
                            {
                                client.Toast(
                                    $"'{ex.Command}' not found in PATH. Install the shell command from {ex.Label} or update the editor command in Settings → Advanced.",
                                    "Editor Not Available",
                                    variant: ToastVariant.Destructive);
                            }
                        })
                );
    }

    private static string FormatPermissionDenials(JobItem job)
    {
        if (job.OutputLines.Count == 0) return "";

        try
        {
            var serializer = new Agents.Runtime.JsonEventSerializer();
            var denials = new List<string>();
            foreach (var line in job.OutputLines)
            {
                var evt = serializer.Deserialize(line);
                if (evt is Agents.Abstractions.PermissionDenialEvent d)
                    denials.Add(d.InputSummary != null ? $"{d.ToolName}({d.InputSummary})" : d.ToolName);
            }
            return denials.Count == 0 ? "" : string.Join("\n", denials);
        }
        catch
        {
            return "Error parsing denials";
        }
    }

    private static string GetPlanId(JobItem job)
    {
        if (!string.IsNullOrEmpty(job.PlanFile))
        {
            var match = Regex.Match(job.PlanFile, @"^(\d{5})-");
            if (match.Success) return match.Groups[1].Value;
        }

        return job.ReportedPlanId ?? job.AllocatedPlanId ?? "";
    }

    private string? GetPlanFolderPath(JobItem job)
    {
        if (string.IsNullOrEmpty(job.PlanFile)) return null;
        var fullPath = Path.Combine(planService.PlansDirectory, job.PlanFile);
        if (Directory.Exists(fullPath)) return fullPath;
        var fallback = job.TypedArgs?.PlanFolder;
        return !string.IsNullOrEmpty(fallback) && Directory.Exists(fallback) ? fallback : null;
    }

    private string? GetPlanLogPath(JobItem job) => FindInLogsDir(job, "*.md");

    private string? GetPlanCliLogPath(JobItem job) => FindInLogsDir(job, $"{job.Id}-*.cli.jsonl", orderByLatest: false);

    private string? FindInLogsDir(JobItem job, string pattern, bool orderByLatest = true)
    {
        var folder = GetPlanFolderPath(job);
        if (string.IsNullOrEmpty(folder)) return null;

        var logsDir = Path.Combine(folder, "Logs");
        if (!Directory.Exists(logsDir)) return null;

        var files = Directory.GetFiles(logsDir, pattern);
        return orderByLatest
            ? files.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : files.FirstOrDefault();
    }

    private string GetPromptwareLearnings(JobItem job)
    {
        var cliLogPath = GetPlanCliLogPath(job);
        if (string.IsNullOrEmpty(cliLogPath) || !File.Exists(cliLogPath))
            return "";

        try
        {
            var paths = new List<string>();
            var promptsRoot = PromptwareHelper.ResolvePromptsRoot(config.TendrilHome);

            foreach (var line in File.ReadLines(cliLogPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonSerializer.Deserialize<JobStatusFile.CliLogEntry>(line, CliLogJsonOptions);
                if (entry is not { ExitCode: 0 }) continue;

                var path = TryResolveWrittenAssetPath(entry.Command, promptsRoot);
                if (path != null)
                    paths.Add(path);
            }

            return paths.Count == 0 ? "" : string.Join("\n", paths);
        }
        catch
        {
            return "";
        }
    }

    private static string? TryResolveWrittenAssetPath(string command, string promptsRoot)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return null;
        if (!parts[0].Equals("promptware", StringComparison.OrdinalIgnoreCase)) return null;

        string subDir;
        if (parts[1].Equals("write-memory", StringComparison.OrdinalIgnoreCase))
            subDir = "Memory";
        else if (parts[1].Equals("write-tool", StringComparison.OrdinalIgnoreCase))
            subDir = "Tools";
        else
            return null;

        var promptwareName = parts[2];
        var filename = Path.GetFileName(parts[3]);
        return Path.Combine(promptsRoot, promptwareName, subDir, filename);
    }

    private static readonly JsonSerializerOptions CliLogJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string? GetPromptwareLogPath(JobItem job)
    {
        var logFile = job.LogFilePath;
        if (!string.IsNullOrEmpty(logFile) && File.Exists(logFile)) return logFile;

        var promptsRoot = PromptwareHelper.ResolvePromptsRoot(config.TendrilHome);
        var logsFolder = Path.Combine(promptsRoot, job.Type, "Logs");
        if (!Directory.Exists(logsFolder)) return null;

        return Directory.GetFiles(logsFolder, "*.md")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private string? GetPromptwareRawLogPath(JobItem job)
    {
        var logPath = GetPromptwareLogPath(job);
        if (logPath == null) return null;
        var rawPath = Path.ChangeExtension(logPath, ".raw.jsonl");
        return File.Exists(rawPath) ? rawPath : null;
    }
}
