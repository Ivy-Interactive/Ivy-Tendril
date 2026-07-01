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
        string promptsRoot)
    {
        _configService = configService;
        _logger = logger;
        _modelPricingService = modelPricingService;
        _planReaderService = planReaderService;
        _telemetryService = telemetryService;
        _planWatcherService = planWatcherService;
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

        // Failed/Timeout is treated like Stop: the work product (worktree) is preserved
        // so the user can inspect or resume. Worktree cleanup happens only on explicit
        // user actions (Delete ExecutePlan, Complete Plan, Reset to Draft).

        HandleWaitForJobsDependents(job, jobs, raiseNotification, startJobSkipDepCheck, persistJob);

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
        // Stop/Delete/Failed/Timeout all revert the plan to where it came from.
        // A cancelled job that the completion path won the race for is handled here too.
        if (job.Status is JobStatus.Failed or JobStatus.Timeout || job.CancellationRequested)
        {
            RevertPlanStateToPrevious(job);
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
            var level = "Feature";
            string? stackHash = null;
            if (Directory.Exists(planFolder))
            {
                var plan = PlanYamlHelper.ReadPlanYaml(planFolder);
                if (plan != null)
                {
                    level = plan.Level;
                    stackHash = _configService?.GetProject(plan.Project)?.StackHash;
                }
            }
            _telemetryService?.TrackPlanCreated(new PlanCreatedContext(level, job.DurationSeconds, job.Provider, stackHash));
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
        if (string.IsNullOrEmpty(job.SessionId))
            return;

        var inlineCost = ExtractCostFromOutputLines(job.OutputLines.ToArray());

        // Fast path: only trust the agent-reported inline cost when it is actually positive.
        // A timed-out/interrupted run emits token usage but no cost, so a zero inline cost must
        // fall through to the pricing fallback below (which derives cost from tokens × model price).
        if (inlineCost is { cost: > 0 })
        {
            ApplyCost(job, persistJob, raisePropertyChanged, inlineCost.Value.tokens, inlineCost.Value.cost);
            return;
        }

        if (_modelPricingService == null)
        {
            // No pricing service to derive cost from: still surface the tokens we have (cost stays
            // null rather than a misleading $0.0000).
            if (inlineCost is { tokens: > 0 })
                ApplyCost(job, persistJob, raisePropertyChanged, inlineCost.Value.tokens, cost: null);
            return;
        }

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
                var (tokens, cost) = ResolveJobCost(inlineCost, costCalc);
                if (tokens > 0 || cost is > 0)
                {
                    if (jobs.TryGetValue(jobId, out var j))
                    {
                        j.Cost = cost;
                        j.Tokens = tokens;
                        persistJob(j);
                        raisePropertyChanged();
                    }

                    if (jobPlanFolder != null)
                        PlanYamlHelper.LogCostToCsv(jobPlanFolder, jobType, tokens, (double)(cost ?? 0m));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate session cost for job {JobId}", jobId);
            }
        });
    }

    /// <summary>
    /// Reconciles the agent-reported inline cost with the pricing-derived session cost.
    /// Prefers any positive cost (inline first, then priced); carries the best available token
    /// count; leaves cost null when neither source has a positive cost so the UI shows nothing
    /// instead of a misleading $0.0000 next to a positive token count.
    /// </summary>
    internal static (int tokens, decimal? cost) ResolveJobCost(
        (int tokens, decimal cost)? inline, CostCalculation? priced)
    {
        var inlineTokens = inline?.tokens ?? 0;
        var inlineCost = inline?.cost ?? 0m;
        var pricedTokens = priced?.TotalTokens ?? 0;
        var pricedCost = priced is not null ? (decimal)priced.TotalCost : 0m;

        // The pricing path re-parses the full session (including subagents), so prefer its token
        // count when present; otherwise fall back to the inline count.
        var tokens = pricedTokens > 0 ? pricedTokens : inlineTokens;

        decimal? cost = inlineCost > 0 ? inlineCost
            : pricedCost > 0 ? pricedCost
            : null;

        return (tokens, cost);
    }

    private void ApplyCost(
        JobItem job,
        Action<JobItem> persistJob,
        Action raisePropertyChanged,
        int tokens,
        decimal? cost)
    {
        job.Cost = cost;
        job.Tokens = tokens;
        persistJob(job);
        raisePropertyChanged();

        var jobPlanFolder = job.TypedArgs?.PlanFolder;
        if (jobPlanFolder != null)
            PlanYamlHelper.LogCostToCsv(jobPlanFolder, job.Type, tokens, (double)(cost ?? 0m));
    }

    private static (int tokens, decimal cost)? ExtractCostFromOutputLines(IReadOnlyList<string> outputLines)
    {
        var serializer = new JsonEventSerializer();
        for (var i = outputLines.Count - 1; i >= 0; i--)
        {
            var evt = serializer.Deserialize(outputLines[i]);
            if (evt is ResultEvent { Usage: { } usage })
            {
                var tokens = usage.InputTokens + usage.OutputTokens;
                var cost = usage.CostUsd ?? 0;
                if (tokens > 0 || cost > 0)
                    return (tokens, cost);
            }
        }
        return null;
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
            FileName = PathHelper.GetPwshPath(),
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
            FileName = PathHelper.GetPwshPath(),
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
            var targetState = hasIncomplete ? PlanStatus.Failed : PlanStatus.Review;

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

            if (TryVerifyByReportedId(job, plansDir) ||
                TryVerifyByOutputRegex(job) ||
                TryVerifyByFilesystem(job, plansDir))
            {
                MoveAttachmentsToPlanFolder(job);
                return;
            }

            if (IsDuplicatePlan(job) || IsInTrash(job)) return;

            MarkCreatePlanFailed(job);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify CreatePlan result for job {JobId}", job.Id);
        }
    }

    private void MoveAttachmentsToPlanFolder(JobItem job)
    {
        if (job.TypedArgs is not CreatePlanArgs cp || string.IsNullOrEmpty(cp.UploadSessionId) || _configService == null || _planReaderService == null)
            return;

        try
        {
            var tempDir = Path.Combine(_configService.TendrilHome, "Attachments", cp.UploadSessionId);
            if (!Directory.Exists(tempDir)) return;

            var planFolder = Path.Combine(_planReaderService.PlansDirectory, job.PlanFile);
            if (!Directory.Exists(planFolder)) return;

            var attachmentsDir = Path.Combine(planFolder, "Attachments");
            Directory.CreateDirectory(attachmentsDir);

            var files = Directory.GetFiles(tempDir);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(attachmentsDir, fileName);
                File.Move(file, destPath, overwrite: true);
            }

            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary session attachments folder: {Dir}", tempDir);
            }

            RewritePathReferences(planFolder, tempDir, attachmentsDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred moving attachments to plan folder for job {JobId}", job.Id);
        }
    }

    private void RewritePathReferences(string planFolder, string oldPath, string newPath)
    {
        try
        {
            var oldPathAlt = oldPath.Replace('\\', '/');
            var newPathAlt = newPath.Replace('\\', '/');

            var files = Directory.GetFiles(planFolder, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".md" or ".yaml" or ".yml")
                {
                    var content = File.ReadAllText(file);
                    var originalContent = content;

                    if (content.Contains(oldPath))
                        content = content.Replace(oldPath, newPath);

                    if (content.Contains(oldPathAlt))
                        content = content.Replace(oldPathAlt, newPathAlt);

                    if (content != originalContent)
                    {
                        File.WriteAllText(file, content);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rewrite attachment path references in folder {Folder}", planFolder);
        }
    }

    private static void MarkCreatePlanFailed(JobItem job)
    {
        job.EnqueueSystemOutput(
            "[Tendril] WARNING: CreatePlan completed but no plan folder or trash entry was found.");
        job.Status = JobStatus.Failed;
        job.StatusMessage = JobFailureAnalyzer.TryReadFailureArtifact(job.OutputLines.ToList())
            ?? job.StatusMessage
            ?? "No plan created";
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

    /// <summary>
    ///     Reverts the plan to the state it had before the job started
    ///     (<see cref="JobItem.PreviousPlanState"/>). Used by Stop, Delete, and
    ///     Failed/Timeout. Falls back to a per-promptware "home" state when the snapshot
    ///     was not captured (e.g. after an app restart).
    /// </summary>
    internal void RevertPlanStateToPrevious(JobItem job)
    {
        try
        {
            var planFolder = job.TypedArgs?.PlanFolder ?? "";
            if (string.IsNullOrEmpty(planFolder)) return;

            var target = job.PreviousPlanState ?? FallbackPreviousState(job.TypedArgs);
            if (target == null) return;

            if (_planReaderService != null)
                _planReaderService.TransitionState(Path.GetFileName(planFolder), target.Value);
            else
                PlanYamlHelper.SetPlanStateByFolder(planFolder, target.Value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revert plan state for job {JobId}", job.Id);
        }
    }

    private static PlanStatus? FallbackPreviousState(JobArgsBase? args) => args switch
    {
        ExecutePlanArgs => PlanStatus.Draft,
        RetryPlanArgs => PlanStatus.Review,
        ExpandPlanArgs or UpdatePlanArgs or SplitPlanArgs => PlanStatus.Draft,
        _ => null
    };

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

    internal void HandleWaitForJobsDependents(
        JobItem completedJob,
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobNotification> raiseNotification,
        Func<JobArgsBase, string> startJobSkipDepCheck,
        Action<JobItem>? persistJob = null)
    {
        var queue = new Queue<JobItem>();
        queue.Enqueue(completedJob);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            var waitingJobs = jobs.Values
                .Where(j => j.Status == JobStatus.Blocked &&
                            j.WaitForJobIds is { Count: > 0 } &&
                            j.WaitForJobIds.Contains(current.Id))
                .ToList();

            foreach (var waitingJob in waitingJobs)
            {
                if (current.Status is JobStatus.Failed or JobStatus.Timeout or JobStatus.Stopped)
                {
                    waitingJob.Status = JobStatus.Failed;
                    waitingJob.StatusMessage = $"Blocked job {current.Id} failed";
                    waitingJob.CompletedAt = DateTime.UtcNow;
                    persistJob?.Invoke(waitingJob);

                    raiseNotification(new JobNotification(
                        "Job Failed",
                        $"{waitingJob.PlanFile}: blocked job {current.Id} failed",
                        false));

                    queue.Enqueue(waitingJob);
                    continue;
                }

                var stillPending = waitingJob.WaitForJobIds!
                    .Any(id => jobs.TryGetValue(id, out var dep) &&
                               dep.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked);

                if (stillPending)
                    continue;

                jobs.TryRemove(waitingJob.Id, out _);
                startJobSkipDepCheck(waitingJob.TypedArgs!);

                raiseNotification(new JobNotification(
                    "Job Unblocked",
                    $"{waitingJob.PlanFile}: all blocking jobs completed, starting",
                    true));
            }
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

    internal (bool Ok, string? BlockReason) CheckDependencies(string planFolder)
        => _dependencyChecker.CheckDependencies(planFolder);

    internal void HandleRetryBlockedJobs(
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobNotification> raiseNotification,
        Func<JobArgsBase, string> startJobSkipDepCheck)
        => _dependencyChecker.RetryBlockedJobs(jobs, raiseNotification, startJobSkipDepCheck);

    internal void WriteJobLog(JobItem job)
    {
        try
        {
            PromptwareLogWriter.WriteLog(job);
        }
        catch
        {
            // ignored
        }

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
            // ignored
        }
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
}
