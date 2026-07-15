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
                    $"I want to debug job {jobId} for what might have gone wrong of what we can improve. Use the /tendril-debug-job skill if available. \n\n";
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
            .Multiline(x => x.JobLog)
            .Multiline(x => x.JobPrompt)
            .Multiline(x => x.JobRawLog)
            .Multiline(x => x.JobEventWireLog)
            .Multiline(x => x.WorkingDirectory)
            .Multiline(x => x.CliCommand)
            .Label(x => x.PromptTitle, "Prompt/Title")
            .Label(x => x.PlanId, "Plan Id")
            .Label(x => x.SessionId, "Session Id")
            .Label(x => x.PlanFolder, "Plan Folder")
            .Label(x => x.JobLog, "Job Log")
            .Label(x => x.JobPrompt, "Job Prompt")
            .Label(x => x.JobRawLog, "Job Raw Log")
            .Label(x => x.JobEventWireLog, "Job Eventwire Log")
            .Label(x => x.PermissionDenials, "Permission Denials")
            .Label(x => x.ExitCode, "Exit Code")
            .Label(x => x.WorkingDirectory, "Working Directory")
            .Label(x => x.CliCommand, "Arguments")
            .Label(x => x.JobId, "Job Id")
            .Builder(x => x.PermissionDenials, f => f.Func((string denials) =>
                string.IsNullOrEmpty(denials) ? null : new CodeBlock(denials)))
            .Builder(x => x.PlanFolder, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.JobLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.JobPrompt, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.JobRawLog, f => f.Func((string path) => PathDropDown(path, copyToClipboard, client)))
            .Builder(x => x.JobEventWireLog,
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
        public required string JobLog { get; init; }
        public required string JobPrompt { get; init; }
        public required string JobRawLog { get; init; }
        public required string JobEventWireLog { get; init; }
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
        JobLog = GetJobLogPath(job) ?? "",
        JobPrompt = GetJobPromptPath(job) ?? "",
        JobRawLog = GetJobRawLogPath(job) ?? "",
        JobEventWireLog = GetJobEventWireLogPath(job) ?? "",
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
                ("Job Log", data.JobLog),
                ("Job Prompt", data.JobPrompt),
                ("Job Raw Log", data.JobRawLog),
                ("Job Eventwire Log", data.JobEventWireLog),
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

    private static string GetPlanId(JobItem job) => job.ResolvePlanId();

    private string? GetPlanFolderPath(JobItem job)
    {
        if (string.IsNullOrEmpty(job.PlanFile)) return null;
        var fullPath = Path.Combine(planService.PlansDirectory, job.PlanFile);
        if (Directory.Exists(fullPath)) return fullPath;
        var fallback = job.TypedArgs?.PlanFolder;
        return !string.IsNullOrEmpty(fallback) && Directory.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// Resolves one of the job's artifacts, or <c>null</c> when it does not exist. Guards an unset
    /// TendrilHome: <see cref="JobLogPaths.JobsDir"/> throws there, and a debug sheet must render blank
    /// path rows rather than take down the app.
    /// </summary>
    private string? ArtifactPath(Func<string, JobItem, string> resolve, JobItem job)
    {
        if (string.IsNullOrWhiteSpace(config.TendrilHome)) return null;
        var path = resolve(config.TendrilHome, job);
        return File.Exists(path) ? path : null;
    }

    private string? GetJobLogPath(JobItem job) => ArtifactPath(JobLogPaths.Log, job);
    private string? GetJobPromptPath(JobItem job) => ArtifactPath(JobLogPaths.Prompt, job);
    private string? GetJobRawLogPath(JobItem job) => ArtifactPath(JobLogPaths.Raw, job);
    private string? GetJobEventWireLogPath(JobItem job) => ArtifactPath(JobLogPaths.EventWire, job);
}
