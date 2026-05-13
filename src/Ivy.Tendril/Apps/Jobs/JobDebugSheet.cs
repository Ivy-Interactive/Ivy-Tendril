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
        var job = jobService.GetJob(jobId);
        if (job is null)
            return Text.P("Job not found.");

        var data = new
        {
            JobId = job.Id,
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
            PromptwareLog = GetPromptwareLogPath(job) ?? "",
            PromptwareRawLog = GetPromptwareRawLogPath(job) ?? "",
            ExitCode = job.ExitCode?.ToString() ?? ""
        };

        return data.ToDetails()
            .Multiline(x => x.PromptTitle, x => x.PermissionDenials)
            .Label(x => x.PromptTitle, "Prompt/Title")
            .Label(x => x.SessionId, "Session Id")
            .Label(x => x.PlanFolder, "Plan Folder")
            .Label(x => x.PlanLog, "Plan Log")
            .Label(x => x.PromptwareLog, "Promptware Log")
            .Label(x => x.PromptwareRawLog, "Promptware Raw Log")
            .Label(x => x.ExitCode, "Exit Code")
            .Label(x => x.JobId, "Job Id")
            .Builder(x => x.PlanFolder, f => f.CopyToClipboard())
            .Builder(x => x.PlanLog, f => f.CopyToClipboard())
            .Builder(x => x.PromptwareLog, f => f.CopyToClipboard())
            .Builder(x => x.PromptwareRawLog, f => f.CopyToClipboard())
            .RemoveEmpty();
    }

    private static string FormatPermissionDenials(JobItem job)
    {
        if (job.OutputLines.Count == 0) return "None";

        try
        {
            var provider = AgentProviderFactory.GetProvider(job.Provider);
            var denials = provider.ExtractPermissionDenials(job.OutputLines.ToArray());
            if (denials.Count == 0) return "None";

            return string.Join("\n", denials.Select(d =>
                d.InputSummary != null ? $"{d.ToolName}({d.InputSummary})" : d.ToolName));
        }
        catch
        {
            return "Error parsing denials";
        }
    }

    private string? GetPlanFolderPath(JobItem job)
    {
        if (string.IsNullOrEmpty(job.PlanFile)) return null;
        var fullPath = Path.Combine(planService.PlansDirectory, job.PlanFile);
        return Directory.Exists(fullPath) ? fullPath : job.TypedArgs?.PlanFolder;
    }

    private string? GetPlanLogPath(JobItem job)
    {
        var folder = GetPlanFolderPath(job);
        if (string.IsNullOrEmpty(folder)) return null;

        var logsDir = Path.Combine(folder, "logs");
        if (!Directory.Exists(logsDir)) return null;

        return Directory.GetFiles(logsDir, "*.md")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
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
