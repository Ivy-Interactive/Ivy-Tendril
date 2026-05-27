using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Jobs;

internal class JobCompletionHandler
{
    private readonly IConfigService? _configService;
    private readonly ILogger _logger;
    private readonly ModelPricingService? _modelPricingService;
    private readonly IPlanReaderService? _planReaderService;
    private readonly IPlanWatcherService? _planWatcherService;
    private readonly ITelemetryService? _telemetryService;
    private readonly IWorktreeLifecycleLogger? _worktreeLifecycleLogger;
    private readonly string _promptsRoot;
    private readonly PlanArtifactSyncer _artifactSyncer;
    private readonly DependencyChecker _dependencyChecker;

    internal JobCompletionHandler(
        IConfigService? configService,
        ILogger logger,
        ModelPricingService? modelPricingService,
        IPlanReaderService? planReaderService,
        ITelemetryService? telemetryService,
        IPlanWatcherService? planWatcherService,
        IWorktreeLifecycleLogger? worktreeLifecycleLogger,
        string promptsRoot)
    {
        _configService = configService;
        _logger = logger;
        _modelPricingService = modelPricingService;
        _planReaderService = planReaderService;
        _telemetryService = telemetryService;
        _planWatcherService = planWatcherService;
        _worktreeLifecycleLogger = worktreeLifecycleLogger;
        _promptsRoot = promptsRoot;
        _artifactSyncer = new PlanArtifactSyncer(configService, logger, planWatcherService);
        _dependencyChecker = new DependencyChecker(planReaderService);
    }

    internal void HandleCompletion(
        JobItem job,
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobItem> persistJob,
        Action<JobNotification> raiseNotification,
        Action raisePropertyChanged,
        Func<JobArgsBase, string> startJobSkipDepCheck)
    {
        var isSuccess = job.Status == JobStatus.Completed;

        SurfacePermissionDenials(job);
        RunAfterHooks(job);
        SendCompletionNotification(job, isSuccess, raiseNotification);
        HandlePlanStateTransition(job, isSuccess);
        TrackTelemetry(job, isSuccess);
        CleanupInboxFile(job);
        CleanupOldTrashFiles();
        WriteJobLog(job);
        NotifyPlanWatcher(job);
        ScheduleCostCalculation(job, jobs, persistJob, raisePropertyChanged);

        if (job.Status is JobStatus.Failed or JobStatus.Timeout)
            ScheduleWorktreeCleanup(job);

        if (job.TypedArgs is ExecutePlanArgs or RetryPlanArgs or CreatePrArgs)
            _dependencyChecker.RetryBlockedJobs(jobs, raiseNotification, startJobSkipDepCheck);

        if (isSuccess && job.TypedArgs is ExecutePlanArgs or RetryPlanArgs or CreatePrArgs or CreateIssueArgs)
        {
            var planFolder = job.TypedArgs?.PlanFolder ?? "";
            _dependencyChecker.RetryBlockedDependents(planFolder, jobs, startJobSkipDepCheck);
        }
    }

    private static void SurfacePermissionDenials(JobItem job)
    {
        if (job.OutputLines.Count == 0) return;

        try
        {
            var denials = ExtractPermissionDenialsFromEventWire(job.OutputLines.ToArray());
            if (denials.Count == 0) return;

            var toolNames = denials.Select(d => d.ToolName).Distinct().ToList();
            var summary = $"Permission denied: {string.Join(", ", toolNames)} ({denials.Count} call{(denials.Count > 1 ? "s" : "")})";

            job.EnqueueSystemOutput($"[Tendril] {summary}");
            foreach (var d in denials.Take(5))
            {
                var detail = d.InputSummary != null ? $"  → {d.ToolName}({d.InputSummary})" : $"  → {d.ToolName}";
                job.EnqueueSystemOutput($"[Tendril] {detail}");
            }
            if (denials.Count > 5)
                job.EnqueueSystemOutput($"[Tendril]   ... and {denials.Count - 5} more");
        }
        catch (Exception)
        {
            // Don't fail completion handling due to denial parsing
        }
    }

    private static IReadOnlyList<PermissionDenialEvent> ExtractPermissionDenialsFromEventWire(IReadOnlyList<string> outputLines)
    {
        var serializer = new JsonEventSerializer();
        var denials = new List<PermissionDenialEvent>();
        foreach (var line in outputLines)
        {
            var evt = serializer.Deserialize(line);
            if (evt is PermissionDenialEvent d)
                denials.Add(d);
        }
        return denials;
    }

    private void RunAfterHooks(JobItem job)
    {
        var planFolderForHooks = job.TypedArgs?.PlanFolder ?? "";
        RunHooks("after", job.Type, planFolderForHooks, job.Project, job);
    }

    private static void SendCompletionNotification(JobItem job, bool isSuccess, Action<JobNotification> raiseNotification)
    {
        var title = job.Status == JobStatus.Timeout ? $"{job.Type} Timed Out" :
            isSuccess ? $"{job.Type} Completed" : $"{job.Type} Failed";
        var message = job.PlanFile ?? job.Type;
        if (!isSuccess && job.StatusMessage != null)
            message += $": {job.StatusMessage}";
        raiseNotification(new JobNotification(title, message, isSuccess));
    }

    private void HandlePlanStateTransition(JobItem job, bool isSuccess)
    {
        if (job.Status is JobStatus.Failed or JobStatus.Timeout)
        {
            ResetPlanState(job);
            return;
        }

        if (!isSuccess) return;

        switch (job.TypedArgs)
        {
            case ExecutePlanArgs:
            case RetryPlanArgs:
                _artifactSyncer.SyncPlanArtifacts(job);
                EnsurePlanStateTransitioned(job);
                break;
            case CreateIssueArgs:
                SetPlanState(job, nameof(PlanStatus.Completed));
                break;
            case UpdatePlanArgs or ExpandPlanArgs:
                SetPlanState(job, nameof(PlanStatus.Draft));
                break;
            case SplitPlanArgs:
                SetPlanState(job, nameof(PlanStatus.Skipped));
                break;
            case CreatePlanArgs:
                VerifyCreatePlanResult(job);
                break;
        }
    }

    private void TrackTelemetry(JobItem job, bool isSuccess)
    {
        if (isSuccess)
            TrackSuccessTelemetry(job);

        _telemetryService?.TrackJobCompleted(job.Type, job.Status, job.DurationSeconds, job.Provider);
        FlushTelemetryAsync();
    }

    private void TrackSuccessTelemetry(JobItem job)
    {
        if (job.TypedArgs is CreatePlanArgs)
        {
            var planFolder = job.TypedArgs?.PlanFolder ?? "";
            var level = "NiceToHave";
            if (Directory.Exists(planFolder))
            {
                var plan = PlanYamlHelper.ReadPlanYaml(planFolder);
                if (plan != null) level = plan.Level;
            }
            _telemetryService?.TrackPlanCreated(new PlanCreatedContext(level, job.DurationSeconds, job.Provider));
        }
        else if (job.TypedArgs is CreatePrArgs)
        {
            _telemetryService?.TrackPrCreated(new PrCreatedContext(job.DurationSeconds, job.Provider));
        }
    }

    private void FlushTelemetryAsync()
    {
        if (_telemetryService == null) return;

        _ = Task.Run(async () =>
        {
            try { await _telemetryService.FlushAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to flush telemetry (best-effort)"); }
        });
    }

    private void NotifyPlanWatcher(JobItem job)
    {
        var notifyFolder = job.TypedArgs?.PlanFolder;
        _planWatcherService?.NotifyChanged(Directory.Exists(notifyFolder) ? notifyFolder : null);
    }

    private void ScheduleCostCalculation(
        JobItem job,
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobItem> persistJob,
        Action raisePropertyChanged)
    {
        var isSuccess = job.Status == JobStatus.Completed;
        if (!isSuccess || _modelPricingService == null || string.IsNullOrEmpty(job.SessionId))
            return;

        var sessionId = job.SessionId;
        var jobPlanFolder = job.TypedArgs?.PlanFolder;
        var jobType = job.Type;
        var jobId = job.Id;
        var provider = job.Provider;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));

            try
            {
                var costCalc = _modelPricingService.CalculateSessionCost(sessionId, provider);
                if (costCalc.TotalCost > 0)
                {
                    if (jobs.TryGetValue(jobId, out var j))
                    {
                        j.Cost = (decimal)costCalc.TotalCost;
                        j.Tokens = costCalc.TotalTokens;
                        persistJob(j);
                        raisePropertyChanged();
                    }

                    if (jobPlanFolder != null)
                        PlanYamlHelper.LogCostToCsv(jobPlanFolder, jobType, costCalc.TotalTokens, costCalc.TotalCost);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate session cost for job {JobId}", jobId);
            }
        });
    }

    internal void RunHooks(string when, string jobType, string planFolder, string project, JobItem job)
    {
        if (_configService == null) return;

        var projectConfig = _configService.GetProject(project);
        if (projectConfig == null) return;

        var hooks = projectConfig.Hooks
            .Where(h => h.When.Equals(when, StringComparison.OrdinalIgnoreCase))
            .Where(h => h.Promptwares.Count == 0 || h.Promptwares.Contains(jobType, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var hook in hooks)
            ExecuteSingleHook(hook, planFolder, job, jobType);
    }

    private void ExecuteSingleHook(PromptwareHookConfig hook, string planFolder, JobItem job, string jobType)
    {
        try
        {
            if (!EvaluateHookCondition(hook, planFolder, job))
                return;

            RunHookAction(hook, planFolder, job, jobType);
        }
        catch (Exception ex)
        {
            job.EnqueueSystemOutput($"[hook:{hook.Name}] Error: {ex.Message}");
        }
    }

    private static bool EvaluateHookCondition(PromptwareHookConfig hook, string planFolder, JobItem job)
    {
        if (string.IsNullOrWhiteSpace(hook.Condition))
            return true;

        var condPsi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {EncodeForPowerShell(hook.Condition)}",
            WorkingDirectory = string.IsNullOrEmpty(planFolder) ? "." : planFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var condProc = Process.Start(condPsi);
        var condOutput = condProc?.StandardOutput.ReadToEnd().Trim() ?? "";
        condProc.WaitForExitOrKill(10000);

        if (condProc?.ExitCode != 0 || condOutput.Equals("False", StringComparison.OrdinalIgnoreCase))
        {
            job.EnqueueSystemOutput($"[hook:{hook.Name}] Condition not met, skipping");
            return false;
        }

        return true;
    }

    private void RunHookAction(PromptwareHookConfig hook, string planFolder, JobItem job, string jobType)
    {
        var actionPsi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {EncodeForPowerShell(hook.Action)}",
            WorkingDirectory = string.IsNullOrEmpty(planFolder) ? "." : planFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        actionPsi.Environment["TENDRIL_JOB_ID"] = job.Id;
        actionPsi.Environment["TENDRIL_JOB_TYPE"] = jobType;
        actionPsi.Environment["TENDRIL_JOB_STATUS"] = job.Status.ToString();
        actionPsi.Environment["TENDRIL_PLAN_FOLDER"] = planFolder;
        actionPsi.Environment["TENDRIL_CONFIG"] = _configService!.ConfigPath;

        using var actionProc = Process.Start(actionPsi);
        var output = actionProc?.StandardOutput.ReadToEnd().Trim() ?? "";
        var stderr = actionProc?.StandardError.ReadToEnd().Trim() ?? "";
        actionProc.WaitForExitOrKill(30000);

        if (!string.IsNullOrEmpty(output))
            job.EnqueueSystemOutput($"[hook:{hook.Name}] {output}");
        if (!string.IsNullOrEmpty(stderr))
            job.EnqueueSystemOutput($"[hook:{hook.Name}] [stderr] {stderr}");

        if (actionProc?.ExitCode != 0)
            job.EnqueueSystemOutput($"[hook:{hook.Name}] Hook failed with exit code {actionProc?.ExitCode}");
    }

    private static string EncodeForPowerShell(string command)
    {
        var bytes = Encoding.Unicode.GetBytes(command);
        return Convert.ToBase64String(bytes);
    }

    private void EnsurePlanStateTransitioned(JobItem job)
    {
        try
        {
            var planFolder = job.TypedArgs?.PlanFolder ?? "";
            var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
            if (planYaml == null) return;

            var hasIncomplete = planYaml.Verifications?
                .Any(v => v.Status is VerificationStatus.Pending or VerificationStatus.Fail) ?? false;
            var targetState = hasIncomplete ? PlanStatus.Failed : PlanStatus.ReadyForReview;

            var folderName = Path.GetFileName(planFolder);
            if (_planReaderService != null)
                _planReaderService.TransitionState(folderName, targetState);
            else
                PlanYamlHelper.SetPlanStateByFolder(planFolder, targetState.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure plan state transition for job {JobId}", job.Id);
        }
    }

    private void SetPlanState(JobItem job, string state)
    {
        try
        {
            var planFolder = job.TypedArgs?.PlanFolder ?? "";

            _logger.LogDebug("SetPlanState: Setting {PlanFolder} to {State} for job {JobId}",
                Path.GetFileName(planFolder), state, job.Id);

            if (_planReaderService != null && Enum.TryParse<PlanStatus>(state, true, out var status))
                _planReaderService.TransitionState(Path.GetFileName(planFolder), status);
            else
                PlanYamlHelper.SetPlanStateByFolder(planFolder, state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set plan state to {State} for job {JobId}", state, job.Id);
        }
    }

    private void VerifyCreatePlanResult(JobItem job)
    {
        try
        {
            var plansDir = _planReaderService?.PlansDirectory;
            if (plansDir == null || !Directory.Exists(plansDir)) return;

            bool isSuccess = false;
            try
            {
                if (TryVerifyByReportedId(job, plansDir) ||
                    TryVerifyByOutputRegex(job) ||
                    TryVerifyByFilesystem(job, plansDir))
                {
                    RelocateUpcomingAttachments(plansDir, job);
                    isSuccess = true;
                    return;
                }

                if (IsDuplicatePlan(job) || IsInTrash(job)) return;

                MarkCreatePlanFailed(job);
            }
            finally
            {
                if (!isSuccess)
                {
                    CleanupUpcomingAttachments(plansDir, job);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify CreatePlan result for job {JobId}", job.Id);
        }
    }

    private static void MarkCreatePlanFailed(JobItem job)
    {
        job.EnqueueSystemOutput(
            "[Tendril] WARNING: CreatePlan completed but no plan folder or trash entry was found.");
        job.Status = JobStatus.Failed;
        job.StatusMessage = JobFailureAnalyzer.TryReadFailureArtifact(job.OutputLines.ToList()) ?? "No plan created";
    }

    private static bool TryVerifyByReportedId(JobItem job, string plansDir)
    {
        if (string.IsNullOrEmpty(job.ReportedPlanId)) return false;

        var reportedFolder = PlanYamlHelper.FindPlanFolderById(plansDir, job.ReportedPlanId);
        if (reportedFolder != null)
        {
            job.PlanFile = reportedFolder;
            return true;
        }

        job.ReportedPlanId = null;
        job.ReportedPlanTitle = null;
        return false;
    }

    private static bool TryVerifyByOutputRegex(JobItem job)
    {
        var outputText = string.Join("\n", job.OutputLines);
        var createdMatch = Regex.Match(outputText, @"Plan created:\s*([\w-]+)");
        if (createdMatch.Success)
        {
            job.PlanFile = createdMatch.Groups[1].Value;
            return true;
        }
        return false;
    }

    private static bool IsDuplicatePlan(JobItem job)
    {
        var outputText = string.Join("\n", job.OutputLines);
        return Regex.IsMatch(outputText, "identified as duplicate:");
    }

    private static bool TryVerifyByFilesystem(JobItem job, string plansDir)
    {
        var planId = job.ReportedPlanId ?? job.AllocatedPlanId;
        var planFolder = PlanYamlHelper.FindPlanFolderById(plansDir, planId);
        if (planFolder != null)
        {
            job.PlanFile = planFolder;
            return true;
        }
        return false;
    }

    private bool IsInTrash(JobItem job)
    {
        var planId = job.ReportedPlanId ?? job.AllocatedPlanId;
        var trashDir = _configService != null
            ? Path.Combine(_configService.TendrilHome, "Trash")
            : null;
        var trashEntry = trashDir != null && !string.IsNullOrEmpty(planId)
            ? PlanYamlHelper.FindTrashEntryById(trashDir, planId)
            : null;
        return trashEntry != null;
    }

    internal void ResetPlanState(JobItem job)
    {
        try
        {
            if (job.TypedArgs is CreatePlanArgs or CreatePrArgs or CreateIssueArgs) return;

            var planFolder = job.TypedArgs?.PlanFolder ?? "";
            var newState = job.TypedArgs is ExecutePlanArgs or RetryPlanArgs ? nameof(PlanStatus.Failed) : nameof(PlanStatus.Draft);
            PlanYamlHelper.SetPlanStateByFolder(planFolder, newState);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset plan state for job {JobId}", job.Id);
        }
    }

    internal void ResetPlanStateToBlocked(JobItem job)
    {
        try
        {
            var planFolder = job.TypedArgs?.PlanFolder ?? "";
            PlanYamlHelper.SetPlanStateByFolder(planFolder, nameof(PlanStatus.Blocked));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset plan state to Blocked for job {JobId}", job.Id);
        }
    }

    internal static void CleanupInboxFile(JobItem job)
    {
        if (string.IsNullOrEmpty(job.InboxFile)) return;
        try
        {
            if (File.Exists(job.InboxFile))
                File.Delete(job.InboxFile);
        }
        catch
        {
        }
    }

    private static void CleanupOldTrashFiles()
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome)) return;

        var trashDir = Path.Combine(tendrilHome, "Trash");
        if (!Directory.Exists(trashDir)) return;

        try
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(7);
            foreach (var file in Directory.GetFiles(trashDir))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
        }
    }

    private void ScheduleWorktreeCleanup(JobItem job)
    {
        if (job.TypedArgs is not (ExecutePlanArgs or RetryPlanArgs)) return;

        var planFolder = job.TypedArgs?.PlanFolder ?? "";
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder)) return;

        var worktreesDir = Path.Combine(planFolder, "Worktrees");
        if (!Directory.Exists(worktreesDir)) return;

        ScheduleWorktreeRemoval(planFolder, worktreesDir);
    }

    private void ScheduleWorktreeRemoval(string planFolder, string worktreesDir)
    {
        var lifecycleLogger = _worktreeLifecycleLogger;
        var logger = _logger;

        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            try
            {
                WorktreeCleanupService.RemoveWorktrees(planFolder, lifecycleLogger: lifecycleLogger);

                if (Directory.Exists(worktreesDir) && Directory.GetDirectories(worktreesDir).Length == 0)
                    Directory.Delete(worktreesDir, false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to cleanup worktrees for {PlanFolder}", Path.GetFileName(planFolder));
            }
        });
    }

    internal (bool Ok, string? BlockReason) CheckDependencies(string planFolder)
        => _dependencyChecker.CheckDependencies(planFolder);

    internal void HandleRetryBlockedJobs(
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobNotification> raiseNotification,
        Func<JobArgsBase, string> startJobSkipDepCheck)
        => _dependencyChecker.RetryBlockedJobs(jobs, raiseNotification, startJobSkipDepCheck);

    private string? ResolvePlanFolder(JobItem job)
    {
        if (job.TypedArgs is not CreatePlanArgs)
            return job.TypedArgs?.PlanFolder;

        var planId = job.ReportedPlanId ?? job.AllocatedPlanId;
        if (string.IsNullOrEmpty(planId)) return null;

        var plansDir = _planReaderService?.PlansDirectory
                       ?? _configService?.PlanFolder;
        if (string.IsNullOrEmpty(plansDir)) return null;

        var folderName = PlanYamlHelper.FindPlanFolderById(plansDir, planId);
        return folderName != null ? Path.Combine(plansDir, folderName) : null;
    }

    internal void WriteJobLog(JobItem job)
    {
        try
        {
            PromptwareLogWriter.WriteLog(job);
            if (!string.IsNullOrEmpty(job.LogFilePath) && job.OutputLines.Count > 0)
                PromptwareLogWriter.WriteRawLog(job.LogFilePath, job.OutputLines);
        }
        catch { }

        try
        {
            MoveStatusFileIfNeeded(job);
        }
        catch { }

        if (_planReaderService == null || string.IsNullOrEmpty(job.PlanFile))
            return;

        if (job.TypedArgs is CreatePlanArgs)
            return;

        try
        {
            var logContent = BuildJobLogContent(job);
            _planReaderService.AddLog(job.PlanFile, job.Type, logContent, job.Id);
            WriteFailedJobOutputIfNeeded(job);
        }
        catch
        {
        }
    }

    private void MoveStatusFileIfNeeded(JobItem job)
    {
        if (string.IsNullOrEmpty(job.StatusFilePath)) return;

        var planFolder = ResolvePlanFolder(job);
        if (!string.IsNullOrEmpty(planFolder) && Directory.Exists(planFolder))
            JobStatusFile.MoveLogToPlanFolder(job.StatusFilePath, planFolder, job.Type, job.Id);
    }

    private string BuildJobLogContent(JobItem job)
    {
        var duration = job.DurationSeconds.HasValue ? $"{job.DurationSeconds}s" : "unknown";
        var logContent = $"# {job.Type}\n\n" +
                         $"- **JobId:** {job.Id}\n" +
                         $"- **Status:** {job.Status}\n" +
                         $"- **Started:** {job.StartedAt:u}\n" +
                         $"- **Completed:** {job.CompletedAt:u}\n" +
                         $"- **Duration:** {duration}\n" +
                         $"- **Provider:** {job.Provider}\n";

        if (!string.IsNullOrEmpty(job.SessionId))
            logContent += $"- **SessionId:** {job.SessionId}\n";

        if (job.Cost.HasValue)
            logContent += $"- **Cost:** ${job.Cost:F4}\n";

        if (job.Tokens.HasValue)
            logContent += $"- **Tokens:** {job.Tokens:N0}\n";

        if (job.Status == JobStatus.Timeout && job.StatusMessage != null)
            logContent += $"- **Timeout Reason:** {job.StatusMessage}\n";

        logContent += BuildPlanOutcomeSummary(job);

        return logContent;
    }

    private void WriteFailedJobOutputIfNeeded(JobItem job)
    {
        if (job.Status is not JobStatus.Failed and not JobStatus.Timeout || job.OutputLines.Count == 0)
            return;

        var planFolder = job.TypedArgs?.PlanFolder;

        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder))
        {
            var logRoot = Path.Combine(
                Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? ".",
                "Logs", "Jobs");
            FileHelper.EnsureDirectory(logRoot);
            planFolder = logRoot;
        }

        var logsDir = Path.Combine(planFolder, "Logs");
        FileHelper.EnsureDirectory(logsDir);
        var outputFile = Path.Combine(logsDir, $"{job.Type}-{job.Id}.output.log");
        File.WriteAllLines(outputFile, job.OutputLines);

        job.EnqueueSystemOutput($"[Tendril] Full output saved to: {outputFile}");
    }

    private static string BuildPlanOutcomeSummary(JobItem job)
    {
        if (job.TypedArgs is not (ExecutePlanArgs or RetryPlanArgs))
            return "";

        var planFolder = job.TypedArgs?.PlanFolder ?? "";
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder))
            return "";

        try
        {
            var plan = PlanYamlHelper.ReadPlanYaml(planFolder);
            if (plan == null)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("\n## Outcome\n");

            AppendCommitsSummary(sb, plan);
            AppendVerificationsSummary(sb, plan);

            sb.AppendLine($"**Final State:** {plan.State}");

            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static void AppendCommitsSummary(StringBuilder sb, PlanYaml plan)
    {
        if (plan.Commits.Count > 0)
        {
            sb.AppendLine($"**Commits:** {plan.Commits.Count}");
            foreach (var commit in plan.Commits)
                sb.AppendLine($"- `{commit}`");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("**Commits:** none\n");
        }
    }

    private static void AppendVerificationsSummary(StringBuilder sb, PlanYaml plan)
    {
        if (plan.Verifications.Count == 0) return;

        sb.AppendLine("**Verifications:**");
        foreach (var v in plan.Verifications)
            sb.AppendLine($"- {v.Name}: {v.Status}");
        sb.AppendLine();
    }

    private void RelocateUpcomingAttachments(string plansDir, JobItem job)
    {
        if (string.IsNullOrEmpty(job.PlanFile)) return;

        var planFolder = Path.Combine(plansDir, job.PlanFile);
        if (!Directory.Exists(planFolder)) return;

        var cp = job.TypedArgs as CreatePlanArgs;
        var description = cp?.Description ?? "";
        if (string.IsNullOrEmpty(description)) return;

        var uploadSessionId = cp?.UploadSessionId;
        if (!string.IsNullOrEmpty(uploadSessionId))
        {
            var sessionDir = Path.Combine(plansDir, uploadSessionId);
            if (Directory.Exists(sessionDir))
            {
                var files = Directory.GetFiles(sessionDir);
                foreach (var tempFilePath in files)
                {
                    var fileName = Path.GetFileName(tempFilePath);
                    var targetFilePath = Path.Combine(planFolder, fileName);

                    try
                    {
                        if (File.Exists(targetFilePath)) File.Delete(targetFilePath);
                        File.Move(tempFilePath, targetFilePath);

                        // Now replace the path in all files in the plan folder
                        ReplaceTextInPlanFiles(planFolder, tempFilePath, targetFilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to relocate upcoming attachment {TempPath} to {TargetPath}", tempFilePath, targetFilePath);
                    }
                }

                // Clean up the temp directory
                try
                {
                    Directory.Delete(sessionDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up temp attachments directory {SessionDir}", sessionDir);
                }
            }
        }
        else
        {
            // Fallback for when UploadSessionId is not available
            var matches = Regex.Matches(description, @"[^\s\]\)]*?[/\\](\d{5})[/\\][^\s\]\)]*");
            if (matches.Count > 0)
            {
                var tempDirsToClean = new HashSet<string>();

                foreach (Match match in matches)
                {
                    var tempFilePath = match.Value.Trim();
                    if (string.IsNullOrEmpty(tempFilePath) || !File.Exists(tempFilePath)) continue;

                    var tempDir = Path.GetDirectoryName(tempFilePath);
                    if (tempDir == null || !Directory.Exists(tempDir)) continue;

                    var dirName = Path.GetFileName(tempDir);
                    if (dirName.Length != 5 || !int.TryParse(dirName, out _)) continue;

                    tempDirsToClean.Add(tempDir);

                    var fileName = Path.GetFileName(tempFilePath);
                    var targetFilePath = Path.Combine(planFolder, fileName);

                    try
                    {
                        if (File.Exists(targetFilePath)) File.Delete(targetFilePath);
                        File.Move(tempFilePath, targetFilePath);

                        // Now replace the path in all files in the plan folder
                        ReplaceTextInPlanFiles(planFolder, tempFilePath, targetFilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to relocate upcoming attachment {TempPath} to {TargetPath}", tempFilePath, targetFilePath);
                    }
                }

                // Clean up the temp directories
                foreach (var tempDir in tempDirsToClean)
                {
                    try
                    {
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to clean up temp attachments directory {TempDir}", tempDir);
                    }
                }
            }
        }
    }

    private void CleanupUpcomingAttachments(string plansDir, JobItem job)
    {
        var cp = job.TypedArgs as CreatePlanArgs;
        if (cp == null) return;

        var uploadSessionId = cp.UploadSessionId;
        if (!string.IsNullOrEmpty(uploadSessionId))
        {
            var sessionDir = Path.Combine(plansDir, uploadSessionId);
            try
            {
                if (Directory.Exists(sessionDir))
                {
                    Directory.Delete(sessionDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to clean up temp attachments directory {SessionDir} on job completion", sessionDir);
            }
        }
        else
        {
            // Fallback: parse description
            var description = cp.Description;
            if (string.IsNullOrEmpty(description)) return;

            var matches = Regex.Matches(description, @"[^\s\]\)]*?[/\\](\d{5})[/\\][^\s\]\)]*");
            foreach (Match match in matches)
            {
                var tempFilePath = match.Value.Trim();
                if (string.IsNullOrEmpty(tempFilePath) || !File.Exists(tempFilePath)) continue;

                var tempDir = Path.GetDirectoryName(tempFilePath);
                if (tempDir == null || !Directory.Exists(tempDir)) continue;

                var dirName = Path.GetFileName(tempDir);
                if (dirName.Length != 5 || !int.TryParse(dirName, out _)) continue;

                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up temp attachments directory {TempDir} on job completion", tempDir);
                }
            }
        }
    }

    private void ReplaceTextInPlanFiles(string planFolder, string oldText, string newText)
    {
        // 1. Update plan.yaml
        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        if (File.Exists(planYamlPath))
        {
            try
            {
                var content = File.ReadAllText(planYamlPath);
                if (content.Contains(oldText))
                {
                    content = content.Replace(oldText, newText);
                    File.WriteAllText(planYamlPath, content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update plan.yaml at {Path}", planYamlPath);
            }
        }

        // 2. Update all revisions
        var revisionsDir = Path.Combine(planFolder, "Revisions");
        if (Directory.Exists(revisionsDir))
        {
            try
            {
                foreach (var file in Directory.GetFiles(revisionsDir, "*.md"))
                {
                    var content = File.ReadAllText(file);
                    if (content.Contains(oldText))
                    {
                        content = content.Replace(oldText, newText);
                        File.WriteAllText(file, content);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update revision files in {Dir}", revisionsDir);
            }
        }
    }
}
