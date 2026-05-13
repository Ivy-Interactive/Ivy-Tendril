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
        var client = UseService<IClientProvider>();

        var job = jobService.GetJob(jobId);
        if (job is null)
            return Text.P("Job not found.");

        var layout = Layout.Vertical().Gap(4).Padding(4);

        layout |= BuildSection("Job Id", job.Id);
        layout |= BuildSection("Prompt/Title", JobsApp.GetFullPrompt(job, planService));
        layout |= BuildSection("Status", $"{job.Status}{(job.StatusMessage != null ? $" — {job.StatusMessage}" : "")}");
        layout |= BuildSection("Type", job.Type);
        layout |= BuildSection("Project", job.Project);
        layout |= BuildSection("Provider", job.Provider);
        layout |= BuildSection("Session Id", job.SessionId ?? "-");

        if (job.StartedAt.HasValue)
            layout |= BuildSection("Started", job.StartedAt.Value.ToString("u"));
        if (job.CompletedAt.HasValue)
            layout |= BuildSection("Completed", job.CompletedAt.Value.ToString("u"));
        if (job.DurationSeconds.HasValue)
            layout |= BuildSection("Duration", $"{job.DurationSeconds}s");
        if (job.Cost.HasValue)
            layout |= BuildSection("Cost", $"${job.Cost:F4}");
        if (job.Tokens.HasValue)
            layout |= BuildSection("Tokens", $"{job.Tokens:N0}");

        layout |= BuildPermissionDenials(job);
        layout |= BuildPathSection("Plan Folder", GetPlanFolderPath(job), copyToClipboard, client);
        layout |= BuildPathSection("Plan Log", GetPlanLogPath(job), copyToClipboard, client);
        layout |= BuildPromptwareLogPaths(job, copyToClipboard, client);

        if (job.ExitCode.HasValue)
            layout |= BuildSection("Exit Code", job.ExitCode.Value.ToString());

        return layout;
    }

    private static object BuildSection(string label, string? value)
    {
        if (string.IsNullOrEmpty(value)) return new Empty();

        return Layout.Vertical().Gap(1)
               | Text.Block(label).Bold()
               | Text.Block(value).Muted();
    }

    private static object BuildPermissionDenials(JobItem job)
    {
        if (job.OutputLines.Count == 0)
            return BuildSection("Permission Denials", "None");

        try
        {
            var provider = AgentProviderFactory.GetProvider(job.Provider);
            var denials = provider.ExtractPermissionDenials(job.OutputLines.ToArray());
            if (denials.Count == 0)
                return BuildSection("Permission Denials", "None");

            var layout = Layout.Vertical().Gap(1);
            layout |= Text.Block("Permission Denials").Bold();
            foreach (var d in denials)
            {
                var detail = d.InputSummary != null
                    ? $"{d.ToolName}({d.InputSummary})"
                    : d.ToolName;
                layout |= Text.Block($"  - {detail}").Muted();
            }
            return layout;
        }
        catch
        {
            return BuildSection("Permission Denials", "Error parsing denials");
        }
    }

    private object BuildPathSection(string label, string? path, Action<string> copy, IClientProvider client)
    {
        if (string.IsNullOrEmpty(path)) return new Empty();

        return Layout.Vertical().Gap(1)
               | Text.Block(label).Bold()
               | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                  | Text.Block(path).Muted()
                  | new Button("Copy").Icon(Icons.ClipboardCopy).Ghost().OnClick(() =>
                  {
                      copy(path);
                      client.Toast($"Copied to clipboard", label);
                  }));
    }

    private object BuildPromptwareLogPaths(JobItem job, Action<string> copy, IClientProvider client)
    {
        var promptsRoot = PromptwareHelper.ResolvePromptsRoot(config.TendrilHome);
        var programFolder = Path.Combine(promptsRoot, job.Type);
        var logsFolder = Path.Combine(programFolder, "Logs");

        if (!Directory.Exists(logsFolder))
            return BuildSection("Promptware Logs", "No logs directory");

        var logFile = job.LogFilePath;
        if (string.IsNullOrEmpty(logFile))
            return BuildSection("Promptware Logs", "No log file recorded");

        var rawFile = Path.ChangeExtension(logFile, ".raw.jsonl");

        var layout = Layout.Vertical().Gap(1);
        layout |= Text.Block("Promptware Logs").Bold();

        layout |= Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                   | Text.Block(logFile).Muted()
                   | new Button("Copy").Icon(Icons.ClipboardCopy).Ghost().OnClick(() =>
                   {
                       copy(logFile);
                       client.Toast("Copied to clipboard", "Log Path");
                   });

        layout |= Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                   | Text.Block(rawFile).Muted()
                   | new Button("Copy").Icon(Icons.ClipboardCopy).Ghost().OnClick(() =>
                   {
                       copy(rawFile);
                       client.Toast("Copied to clipboard", "Raw Log Path");
                   });

        return layout;
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

        var latestLog = Directory.GetFiles(logsDir, "*.md")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return latestLog;
    }

}
