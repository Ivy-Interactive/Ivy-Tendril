using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Agents;

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

        var job = jobService.GetJob(jobId);
        if (job is null)
            return Text.P("Job not found.");

        var data = new
        {
            JobId = job.Id,
            PlanId = GetPlanId(job),
            PromptTitle = JobsApp.GetFullPrompt(job, planService) ?? "",
            Status = $"{job.Status}{(job.StatusMessage != null ? $" — {job.StatusMessage}" : "")}",
            job.Type,
            job.Project,
            job.Provider,
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
            PromptwareLog = GetPromptwareLogPath(job) ?? "",
            PromptwareRawLog = GetPromptwareRawLogPath(job) ?? "",
            ExitCode = job.ExitCode?.ToString() ?? ""
        };

        return data.ToDetails()
            .Multiline(x => x.PromptTitle)
            .Multiline(x => x.PermissionDenials)
            .Label(x => x.PromptTitle, "Prompt/Title")
            .Label(x => x.PlanId, "Plan Id")
            .Label(x => x.SessionId, "Session Id")
            .Label(x => x.PlanFolder, "Plan Folder")
            .Label(x => x.PlanLog, "Plan Log")
            .Label(x => x.PlanCliLog, "Plan CLI Log")
            .Label(x => x.PromptwareLog, "Promptware Log")
            .Label(x => x.PromptwareRawLog, "Promptware Raw Log")
            .Label(x => x.PermissionDenials, "Permission Denials")
            .Label(x => x.ExitCode, "Exit Code")
            .Label(x => x.JobId, "Job Id")
            .Builder(x => x.PermissionDenials, f => f.Func((string denials) =>
                new CodeBlock(denials)))
            .Builder(x => x.PlanFolder, f => f.Func((string path) => PathDropDown(path, copyToClipboard)))
            .Builder(x => x.PlanLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard)))
            .Builder(x => x.PlanCliLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard)))
            .Builder(x => x.PromptwareLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard)))
            .Builder(x => x.PromptwareRawLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard)))
            .RemoveEmpty();
    }

    private object PathDropDown(string path, Action<string> copyToClipboard)
    {
        return Layout.Horizontal().Gap(2).AlignContent(Align.Center)
            | Text.Block(path)
            | new Button().Icon(Icons.EllipsisVertical).Ghost().Small()
                .WithDropDown(
                    new MenuItem("Copy to Clipboard", Icon: Icons.ClipboardCopy, Tag: "Copy")
                        .OnSelect(() => copyToClipboard(path)),
                    new MenuItem($"Open in {config.Editor.Label}", Icon: Icons.Code, Tag: "OpenInEditor")
                        .OnSelect(() => config.OpenInEditor(path))
                );
    }

    private static string FormatPermissionDenials(JobItem job)
    {
        if (job.OutputLines.Count == 0) return "";

        try
        {
            var provider = AgentProviderFactory.GetProvider(job.Provider);
            var denials = provider.ExtractPermissionDenials(job.OutputLines.ToArray());
            if (denials.Count == 0) return "";

            return string.Join("\n", denials.Select(d =>
                d.InputSummary != null ? $"{d.ToolName}({d.InputSummary})" : d.ToolName));
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
        return Directory.Exists(fullPath) ? fullPath : job.TypedArgs?.PlanFolder;
    }

    private string? GetPlanLogPath(JobItem job) => FindInLogsDir(job, "*.md");

    private string? GetPlanCliLogPath(JobItem job) => FindInLogsDir(job, $"{job.Id}-*.cli.jsonl", orderByLatest: false);

    private string? FindInLogsDir(JobItem job, string pattern, bool orderByLatest = true)
    {
        var folder = GetPlanFolderPath(job);
        if (string.IsNullOrEmpty(folder)) return null;

        var logsDir = Path.Combine(folder, "logs");
        if (!Directory.Exists(logsDir)) return null;

        var files = Directory.GetFiles(logsDir, pattern);
        return orderByLatest
            ? files.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : files.FirstOrDefault();
    }

    private string? GetPromptwareLogPath(JobItem job)
    {
        var logFile = job.LogFilePath;
        if (!string.IsNullOrEmpty(logFile)) return logFile;

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
        return logPath != null ? Path.ChangeExtension(logPath, ".raw.jsonl") : null;
    }
}
