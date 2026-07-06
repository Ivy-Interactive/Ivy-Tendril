using System.Text.RegularExpressions;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Jobs.Dialogs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Views.Sheets;

public class JobDebugSheet(
    string jobId,
    IJobService jobService,
    IPlanReaderService planService,
    IConfigService config,
    Action closeSheet) : ViewBase
{
    public override object Build()
    {
        var copyToClipboard = UseClipboard();
        var client = UseService<IClientProvider>();
        var nav = UseNavigation();
        var agentRunner = UseService<IAgentRunner>();

        var (reportBugDialog, showReportBugDialog) = UseTrigger((isOpen) =>
            !isOpen.Value ? null : new ReportBugDialog(isOpen, jobId));

        #if DEBUG
        var (debugAgentDialog, showDebugAgentDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            var job = jobService.GetJob(jobId);
            if (job is null) return null;
            // Snapshot branding + details while the job is live, so confirming always navigates
            // even if the job is evicted between opening the dialog and clicking the button.
            var branding = AgentBranding.For(config.Settings.CodingAgent, agentRunner);
            var details = FormatCopyDetails(BuildData(job));
            return new DebugWithAgentDialog(isOpen, branding, focus =>
            {
                var prompt =
                    "I want to debug the following job for what might have gone wrong of what we can improve. Use the /tendril-debug-job skill if available. \n\n";
                if (!string.IsNullOrEmpty(focus))
                    prompt += $"In particular, focus on: {focus}\n\n";
                prompt += details;
                nav.Navigate<AgentApp>(new AgentAppArgs(prompt));
                closeSheet();
            });
        });
        #endif

        var job = jobService.GetJob(jobId);
        if (job is null)
            return Text.P("Job not found.");

        var data = BuildData(job);

        var detailsView = data.ToDetails()
            .RemoveEmpty()
            .Multiline(x => x.PromptTitle)
            .Multiline(x => x.PermissionDenials)
            .Multiline(x => x.Status)
            .Multiline(x => x.PlanFolder)
            .Multiline(x => x.PlanLog)
            .Multiline(x => x.PromptwareLog)
            .Multiline(x => x.PromptwareRawLog)
            .Multiline(x => x.WorkingDirectory)
            .Multiline(x => x.CliCommand)
            .Label(x => x.PromptTitle, "Prompt/Title")
            .Label(x => x.PlanId, "Plan Id")
            .Label(x => x.SessionId, "Session Id")
            .Label(x => x.PlanFolder, "Plan Folder")
            .Label(x => x.PlanLog, "Plan Log")
            .Label(x => x.PromptwareLog, "Promptware Log")
            .Label(x => x.PromptwareRawLog, "Promptware Raw Log")
            .Label(x => x.PermissionDenials, "Permission Denials")
            .Label(x => x.ExitCode, "Exit Code")
            .Label(x => x.WorkingDirectory, "Working Directory")
            .Label(x => x.CliCommand, "Arguments")
            .Label(x => x.JobId, "Job Id")
            .Builder(x => x.PermissionDenials, f => f.Func((string denials) =>
                string.IsNullOrEmpty(denials) ? null : new CodeBlock(denials)))
            .Builder(x => x.PlanFolder, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.PlanLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.PromptwareLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.PromptwareRawLog,
                f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.WorkingDirectory, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.CliCommand, f => f.Func((string cmd) => new CodeBlock(cmd).WrapLines()))
            .Builder(x => x.JobId, f => f.CopyToClipboard())
            .Builder(x => x.PlanId, f => f.CopyToClipboard());

        // Copy/debug text is projected from the same `data` model the details view renders,
        // so the field values stay in sync with the sheet (labels are defined per-view; the
        // `.Label()` API takes lambda expressions and can't be shared with the copy projection).
        var copyDetails = FormatCopyDetails(data);

        var agentBranding = AgentBranding.For(config.Settings.CodingAgent, agentRunner);

        var header = Layout.Horizontal().Gap(2)
                     | new Button("Copy Details").Icon(Icons.ClipboardCopy).Outline().OnClick(() =>
                     {
                         copyToClipboard(copyDetails);
                         client.Toast("Job details copied to clipboard", "Copied");
                     })
                     | new Button("Report Bug").Icon(Icons.Bug).OnClick(() => showReportBugDialog());

        #if DEBUG
        header |= new Button($"Debug with {agentBranding.Label}").Icon(agentBranding.Icon).Outline()
            .OnClick(() => showDebugAgentDialog());
        #endif

        return new Fragment(
            new HeaderLayout(header, detailsView).Size(Size.Full()),
            reportBugDialog
            #if DEBUG
            , debugAgentDialog
            #endif
        );
    }

    // Master model for the sheet: the details view renders it, and FormatCopyDetails projects it
    // to text. Property order here is the details-view display order.
    private sealed record JobDebugData
    {
        public required string JobId { get; init; }
        public required string PlanId { get; init; }
        public required string PromptTitle { get; init; }
        public required string Status { get; init; }
        public required string Type { get; init; }
        public required string Project { get; init; }
        public required string Provider { get; init; }
        public required string Model { get; init; }
        public required string SessionId { get; init; }
        public required string Started { get; init; }
        public required string Completed { get; init; }
        public required string Duration { get; init; }
        public required string Cost { get; init; }
        public required string Tokens { get; init; }
        public required string PermissionDenials { get; init; }
        public required string PlanFolder { get; init; }
        public required string PlanLog { get; init; }
        public required string PromptwareLog { get; init; }
        public required string PromptwareRawLog { get; init; }
        public required string WorkingDirectory { get; init; }
        public required string CliCommand { get; init; }
        public required string ExitCode { get; init; }
    }

    private JobDebugData BuildData(JobItem job) => new()
    {
        JobId = job.Id,
        PlanId = GetPlanId(job),
        PromptTitle = JobsApp.GetFullPrompt(job, planService) ?? "",
        Status = $"{job.Status}{(job.StatusMessage != null ? $": {job.StatusMessage}" : "")}",
        Type = job.Type,
        Project = job.Project,
        Provider = job.Provider,
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
        PromptwareLog = GetPromptwareLogPath(job) ?? "",
        PromptwareRawLog = GetPromptwareRawLogPath(job) ?? "",
        WorkingDirectory = job.WorkingDirectory ?? "",
        CliCommand = job.CliCommand ?? "",
        ExitCode = job.ExitCode?.ToString() ?? "",
    };

    // Projects the master model to the copied/debug text. Labels mirror the details-view labels;
    // paths and logs are grouped last for a readable paste.
    private static string FormatCopyDetails(JobDebugData data) =>
        string.Join("\n", new (string Label, string Value)[]
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
                ("Promptware Log", data.PromptwareLog),
                ("Promptware Raw Log", data.PromptwareRawLog),
            }
            .Where(l => !string.IsNullOrEmpty(l.Value))
            .Select(l => $"{l.Label}: {l.Value}"));

    private object PathDropDown(string path, Action<string> copyToClipboard, IClientProvider client)
    {
        return Layout.Horizontal().Gap(2).Width(Size.Full()).AlignContent(Align.SpaceBetween)
            | Text.Block(path).Width(Size.Grow())
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
