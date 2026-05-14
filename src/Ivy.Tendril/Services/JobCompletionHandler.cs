using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Agents;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

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

        if (job.TypedArgs is ExecutePlanArgs or CreatePrArgs)
            _dependencyChecker.RetryBlockedJobs(jobs, raiseNotification, startJobSkipDepCheck);

        if (isSuccess && job.TypedArgs is ExecutePlanArgs or CreatePrArgs or CreateIssueArgs)
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
            var provider = AgentProviderFactory.GetProvider(job.Provider);
            var denials = provider.ExtractPermissionDenials(job.OutputLines.ToArray());
            if (denials.Count == 0) return;

            var toolNames = denials.Select(d => d.ToolName).Distinct().ToList();
            var summary = $"Permission denied: {string.Join(", ", toolNames)} ({denials.Count} call{(denials.Count > 1 ? "s" : "")})";

            job.EnqueueOutput($"[Tendril] {summary}");
            foreach (var d in denials.Take(5))
            {
                var detail = d.InputSummary != null ? $"  → {d.ToolName}({d.InputSummary})" : $"  → {d.ToolName}";
                job.EnqueueOutput($"[Tendril] {detail}");
            }
            if (denials.Count > 5)
                job.EnqueueOutput($"[Tendril]   ... and {denials.Count - 5} more");
        }
        catch (Exception)
        {
            // Don't fail completion handling due to denial parsing
        }
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
                _artifactSyncer.SyncPlanArtifacts(job);
                EnsurePlanStateTransitioned(job);
                break;
            case CreateIssueArgs:
                SetPlanState(job, "Completed");
                break;
            case UpdatePlanArgs or ExpandPlanArgs:
                SetPlanState(job, "Draft");
                break;
            case SplitPlanArgs:
                SetPlanState(job, "Skipped");
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

        _telemetryService?.TrackJobCompleted(job.Type, job.Status, job.DurationSeconds);
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
            _telemetryService?.TrackPlanCreated(new PlanCreatedContext(level, job.DurationSeconds));
        }
        else if (job.TypedArgs is CreatePrArgs)
        {
            _telemetryService?.TrackPrCreated(new PrCreatedContext(job.DurationSeconds));
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
            job.EnqueueOutput($"[hook:{hook.Name}] Error: {ex.Message}");
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
            job.EnqueueOutput($"[hook:{hook.Name}] Condition not met, skipping");
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
            job.EnqueueOutput($"[hook:{hook.Name}] {output}");
        if (!string.IsNullOrEmpty(stderr))
            job.EnqueueOutput($"[hook:{hook.Name}] [stderr] {stderr}");

        if (actionProc?.ExitCode != 0)
            job.EnqueueOutput($"[hook:{hook.Name}] Hook failed with exit code {actionProc?.ExitCode}");
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

            if (planYaml.State is "Executing" or "Building" or "Draft")
            {
                var hasIncomplete = planYaml.Verifications?
                    .Any(v => v.Status is "Pending" or "Fail") ?? false;
                var targetState = hasIncomplete ? PlanStatus.Failed : PlanStatus.ReadyForReview;

                var folderName = Path.GetFileName(planFolder);
                if (_planReaderService != null)
                    _planReaderService.TransitionState(folderName, targetState);
                else
                    PlanYamlHelper.SetPlanStateByFolder(planFolder, targetState.ToString());
            }
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

            if (TryVerifyByReportedId(job, plansDir)) return;
            if (TryVerifyByOutputRegex(job)) return;
            if (IsDuplicatePlan(job)) return;
            if (TryVerifyByFilesystem(job, plansDir)) return;
            if (IsInTrash(job)) return;

            MarkCreatePlanFailed(job);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify CreatePlan result for job {JobId}", job.Id);
        }
    }

    private static void MarkCreatePlanFailed(JobItem job)
    {
        job.EnqueueOutput(
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
        var createdMatch = Regex.Match(outputText, @"Plan created:\s*(\S+)");
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
            var newState = job.TypedArgs is ExecutePlanArgs ? "Failed" : "Draft";
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
            PlanYamlHelper.SetPlanStateByFolder(planFolder, "Blocked");
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
        if (job.TypedArgs is not ExecutePlanArgs) return;

        var planFolder = job.TypedArgs?.PlanFolder ?? "";
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder)) return;

        var worktreesDir = Path.Combine(planFolder, "worktrees");
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

        var logsDir = Path.Combine(planFolder, "logs");
        FileHelper.EnsureDirectory(logsDir);
        var outputFile = Path.Combine(logsDir, $"{job.Type}-{job.Id}.output.log");
        File.WriteAllLines(outputFile, job.OutputLines);

        job.EnqueueOutput($"[Tendril] Full output saved to: {outputFile}");
    }

    private static string BuildPlanOutcomeSummary(JobItem job)
    {
        if (job.TypedArgs is not ExecutePlanArgs)
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
}
