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
        Func<JobArgsBase, string> startJobSkipDepCheck,
        Action<string>? deleteJob = null)
    {
        var isSuccess = job.Status == JobStatus.Completed;

        SurfacePermissionDenials(job);
        RunAfterHooks(job);
        SendCompletionNotification(job, isSuccess, raiseNotification);
        HandlePlanStateTransition(job, isSuccess);
        TrackTelemetry(job, isSuccess);
        CleanupInboxFile(job);
        WriteJobLog(job);
        NotifyPlanWatcher(job);
        ScheduleCostCalculation(job, jobs, persistJob, raisePropertyChanged);

        // Failed/Timeout is treated like Stop: the work product (worktree) is preserved
        // so the user can inspect or resume. Worktree cleanup happens only on explicit
        // user actions (Delete ExecutePlan, Complete Plan, Reset to Draft).

        HandleWaitForJobsDependents(job, jobs, raiseNotification, startJobSkipDepCheck, persistJob, deleteJob);

        if (job.TypedArgs is ExecutePlanArgs or RetryPlanArgs or CreatePrArgs)
            _dependencyChecker.RetryBlockedJobs(jobs, raiseNotification, startJobSkipDepCheck, deleteJob, persistJob);

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
                if (job.TypedArgs is UpdatePlanArgs)
                {
                    MoveAttachmentsToPlanFolder(job);
                }
                SetPlanState(job, nameof(PlanStatus.Draft));
                break;
            case SplitPlanArgs:
                SetPlanState(job, nameof(PlanStatus.Skipped));
                break;
            case CreatePlanArgs:
                VerifyCreatePlanResult(job);
                break;
            case CreatePrArgs:
                ReconcileCreatePrResult(job);
                break;
        }
    }

    private void TrackTelemetry(JobItem job, bool isSuccess)
    {
        if (isSuccess)
            TrackSuccessTelemetry(job);

        _telemetryService?.TrackJobCompleted(job.Type, job.Status, job.DurationSeconds, job.Provider,
            job.ResolvePlanId());
        FlushTelemetryAsync();
    }

    private void TrackSuccessTelemetry(JobItem job)
    {
        if (job.TypedArgs is CreatePlanArgs createPlanArgs)
        {
            // CreatePlanArgs has no PlanFolder — the folder does not exist yet when the job starts.
            // VerifyCreatePlanResult (which runs earlier in HandleCompletion) records the resolved
            // folder *name* in job.PlanFile, so resolve it against the plans directory here.
            var plansDir = _planReaderService?.PlansDirectory;
            var planFolder = !string.IsNullOrEmpty(plansDir) && !string.IsNullOrEmpty(job.PlanFile)
                ? Path.Combine(plansDir, job.PlanFile)
                : "";

            var level = "Feature";
            // Fall back to the project the job was queued for, so stack_hash still lands even when
            // the plan folder cannot be resolved.
            var stackHash = _configService?.GetProject(createPlanArgs.Project)?.StackHash;
            if (Directory.Exists(planFolder))
            {
                var plan = PlanYamlHelper.ReadPlanYaml(planFolder);
                if (plan != null)
                {
                    level = plan.Level;
                    stackHash = _configService?.GetProject(plan.Project)?.StackHash ?? stackHash;
                }
            }
            _telemetryService?.TrackPlanCreated(new PlanCreatedContext(level, job.DurationSeconds, job.Provider,
                stackHash, job.ResolvePlanId()));
        }
        else if (job.TypedArgs is CreatePrArgs)
        {
            _telemetryService?.TrackPrCreated(new PrCreatedContext(job.DurationSeconds, job.Provider,
                job.ResolvePlanId()));
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

        var inlineUsage = ExtractCostFromOutputLines(job.OutputLines.ToArray());

        // Fast path: only trust the agent-reported inline cost when it is actually positive.
        // A timed-out/interrupted run emits token usage but no cost, so a zero inline cost must
        // fall through to the pricing fallback below (which derives cost from tokens × model price).
        if (inlineUsage is { CostUsd: > 0 })
        {
            ApplyCost(job, persistJob, raisePropertyChanged, ResolveJobCost(inlineUsage, priced: null));
            return;
        }

        if (_modelPricingService == null)
        {
            // No pricing service to derive cost from: still surface the tokens we have (cost stays
            // null rather than a misleading $0.0000).
            var inlineOnly = ResolveJobCost(inlineUsage, priced: null);
            if (inlineOnly.Tokens > 0)
                ApplyCost(job, persistJob, raisePropertyChanged, inlineOnly);
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
                var usage = ResolveJobCost(inlineUsage, costCalc);
                if (usage.Tokens > 0 || usage.Cost is > 0)
                {
                    if (jobs.TryGetValue(jobId, out var j))
                    {
                        usage.ApplyTo(j);
                        persistJob(j);
                        raisePropertyChanged();
                    }

                    if (jobPlanFolder != null)
                        PlanYamlHelper.LogCostToCsv(jobPlanFolder, jobType, usage.Tokens, (double)(usage.Cost ?? 0m));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate session cost for job {JobId}", jobId);
            }
        });
    }

    /// <summary>
    /// Reconciles the agent-reported inline usage with the pricing-derived session cost.
    /// Prefers any positive cost (inline first, then priced) and records which of the two it came
    /// from; carries the best available token count and per-bucket breakdown; leaves cost null when
    /// neither source has a positive cost so the UI shows nothing instead of a misleading $0.0000
    /// next to a positive token count.
    /// </summary>
    internal static JobUsageSnapshot ResolveJobCost(AgentUsage? inline, CostCalculation? priced)
    {
        var inlineTokens = inline is not null ? inline.InputTokens + inline.OutputTokens : 0;
        var inlineCost = inline?.CostUsd ?? 0m;
        var pricedTokens = priced?.TotalTokens ?? 0;
        var pricedCost = priced is not null ? (decimal)priced.TotalCost : 0m;

        // The pricing path re-parses the full session (including subagents), so prefer its token
        // count when present; otherwise fall back to the inline count.
        var tokens = pricedTokens > 0 ? pricedTokens : inlineTokens;

        decimal? cost = inlineCost > 0 ? inlineCost
            : pricedCost > 0 ? pricedCost
            : null;

        var costSource = inlineCost > 0 ? JobCostSources.Agent
            : pricedCost > 0 ? JobCostSources.Computed
            : null;

        // Same precedence for the buckets: the session parse sees subagent traffic the inline
        // ResultEvent never reports, so it wins whenever it found any tokens at all (cache-only
        // sessions included, which is why this doesn't key off TotalTokens).
        var pricedHasBuckets = priced is not null &&
                               (priced.InputTokens > 0 || priced.OutputTokens > 0 ||
                                priced.CacheReadTokens > 0 || priced.CacheWriteTokens > 0);

        return new JobUsageSnapshot
        {
            Tokens = tokens,
            Cost = cost,
            CostSource = costSource,
            Model = priced?.Model ?? inline?.Model,
            InputTokens = pricedHasBuckets ? priced!.InputTokens : inline?.InputTokens,
            OutputTokens = pricedHasBuckets ? priced!.OutputTokens : inline?.OutputTokens,
            CacheReadTokens = pricedHasBuckets ? priced!.CacheReadTokens : inline?.CacheReadTokens,
            CacheWriteTokens = pricedHasBuckets ? priced!.CacheWriteTokens : inline?.CacheWriteTokens,
            // Only the inline ResultEvent carries reasoning tokens; SessionCostResult has no such
            // bucket, so this is never sourced from the priced path.
            ReasoningTokens = inline?.ReasoningTokens,
        };
    }

    private void ApplyCost(
        JobItem job,
        Action<JobItem> persistJob,
        Action raisePropertyChanged,
        JobUsageSnapshot usage)
    {
        usage.ApplyTo(job);
        persistJob(job);
        raisePropertyChanged();

        var jobPlanFolder = job.TypedArgs?.PlanFolder;
        if (jobPlanFolder != null)
            PlanYamlHelper.LogCostToCsv(jobPlanFolder, job.Type, usage.Tokens, (double)(usage.Cost ?? 0m));
    }

    /// <summary>
    /// Returns the whole usage report from the agent's last ResultEvent (not just the two headline
    /// numbers), so the cache/reasoning buckets and the model survive to the breakdown sheet.
    /// </summary>
    private static AgentUsage? ExtractCostFromOutputLines(IReadOnlyList<string> outputLines)
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
                    return usage;
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
        if (condProc == null)
        {
            job.EnqueueSystemOutput($"[hook:{hook.Name}] Could not start condition process, skipping");
            return false;
        }
        ChildProcessTracker.AddProcess(condProc);

        // Read both streams concurrently and start the reads BEFORE waiting: a blocking ReadToEnd()
        // would never return for a hung hook, making the timeout below dead code, and reading one
        // stream to EOF before the other can deadlock if the child floods the unread pipe (#1455
        // class). The reads complete when the pipes close on exit or kill.
        var condOutTask = condProc.StandardOutput.ReadToEndAsync();
        var condErrTask = condProc.StandardError.ReadToEndAsync();
        var condExitedNormally = condProc.WaitForExitOrKill(10000);
        var condOutput = HarvestHookStream(condOutTask);
        _ = HarvestHookStream(condErrTask); // drain to prevent a full stderr pipe from wedging the read

        if (!condExitedNormally)
        {
            job.EnqueueSystemOutput($"[hook:{hook.Name}] Condition timed out after 10s and was terminated, skipping");
            return false;
        }

        if (condProc.ExitCode != 0 || condOutput.Equals("False", StringComparison.OrdinalIgnoreCase))
        {
            job.EnqueueSystemOutput($"[hook:{hook.Name}] Condition not met, skipping");
            return false;
        }

        return true;
    }

    // Harvests a stdout/stderr read that was started before WaitForExitOrKill. The read completes
    // when the pipe closes (process exit or kill); the bounded wait ensures a wedged pipe cannot
    // re-introduce the hang the concurrent-reads-plus-timeout pattern is meant to prevent.
    private static string HarvestHookStream(Task<string> readTask)
    {
        try
        {
            return readTask.Wait(TimeSpan.FromSeconds(5)) ? readTask.Result.Trim() : "";
        }
        catch
        {
            return "";
        }
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
        if (actionProc == null)
        {
            job.EnqueueSystemOutput($"[hook:{hook.Name}] Could not start hook process");
            return;
        }
        ChildProcessTracker.AddProcess(actionProc);

        // Read both streams concurrently and start the reads BEFORE waiting: a blocking ReadToEnd()
        // would never return for a hung hook, making the timeout below dead code, and reading one
        // stream to EOF before the other can deadlock if the child floods the unread pipe (#1455
        // class). The reads complete when the pipes close on exit or kill.
        var outTask = actionProc.StandardOutput.ReadToEndAsync();
        var errTask = actionProc.StandardError.ReadToEndAsync();
        var exitedNormally = actionProc.WaitForExitOrKill(30000);
        var output = HarvestHookStream(outTask);
        var stderr = HarvestHookStream(errTask);

        if (!string.IsNullOrEmpty(output))
            job.EnqueueSystemOutput($"[hook:{hook.Name}] {output}");
        if (!string.IsNullOrEmpty(stderr))
            job.EnqueueSystemOutput($"[hook:{hook.Name}] [stderr] {stderr}");

        if (!exitedNormally)
            job.EnqueueSystemOutput($"[hook:{hook.Name}] Hook timed out after 30s and was terminated");
        else if (actionProc.ExitCode != 0)
            job.EnqueueSystemOutput($"[hook:{hook.Name}] Hook failed with exit code {actionProc.ExitCode}");
    }

    private static string EncodeForPowerShell(string command)
    {
        var bytes = Encoding.Unicode.GetBytes(command);
        return Convert.ToBase64String(bytes);
    }

    internal void EnsurePlanStateTransitioned(JobItem job)
    {
        try
        {
            var planFolder = job.TypedArgs?.PlanFolder ?? "";
            var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
            if (planYaml == null) return;

            // A failed pre-execution means the plan's premise was checked and rejected, so nothing
            // was implemented. That is decisive regardless of the verification rows: the agent may
            // have left them Pending (which hasIncomplete already catches) or set them all Skipped
            // for a plan it never executed, which would otherwise route to Review and from there be
            // one click from Completed with zero commits. Absent or unparseable report, Pass and
            // Skipped all fall through to the verification-only decision. See plan 00103.
            var preExecution = PlanYamlHelper.ReadPreExecutionResult(planFolder);
            var hasIncomplete = planYaml.Verifications?
                .Any(v => v.Status is VerificationStatus.Pending or VerificationStatus.Fail) ?? false;
            var targetState = preExecution == VerificationStatus.Fail || hasIncomplete
                ? PlanStatus.Failed
                : PlanStatus.Review;

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

    internal static readonly Regex GitHubPrUrlPattern = new(
        @"https?://github\.com/(?<owner>[^/\s]+)/(?<repo>[^/\s]+)/pull/(?<number>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    ///     Safety net for CreatePr. The agent is supposed to record each PR URL via
    ///     `tendril plan add-pr` and set the plan to Completed (Program.md step 6), but a rushed or
    ///     weak provider can stop after opening the PR and skip that closeout — leaving the PR
    ///     invisible in the Pull Requests app (which filters on Prs.Count > 0) and the plan stuck in
    ///     Drafts. Here we parse PR URLs from the job output, record the ones missing from plan.yaml,
    ///     and mark the plan Completed. A plan that has a PR is Completed regardless of PrMerge; merge
    ///     is tracked separately as PR status.
    ///
    ///     Only PR URLs whose repo matches one of this plan's repos are trusted — a foreign or
    ///     SourceUrl PR merely echoed in the transcript must not be recorded or force-complete the
    ///     plan. Plans with no repos (e.g. direct-to-main scaffolding, which opens no PR) are left
    ///     untouched. No-ops cleanly when the agent already did the closeout.
    /// </summary>
    internal void ReconcileCreatePrResult(JobItem job)
    {
        try
        {
            var planFolder = job.TypedArgs?.PlanFolder ?? "";
            if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder))
                return;

            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Repos configured for this plan, by folder name — the only repos a PR may target.
            var planRepoNames = plan.Repos
                .Select(r => Path.GetFileName(r.TrimEnd('/', '\\')))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Canonical identity (owner/repo#number) of PRs already recorded, so a differently
            // formatted duplicate (trailing slash, /files suffix, ...) isn't re-added.
            var recordedKeys = plan.Prs
                .Select(p => GitHubPrUrlPattern.Match(p))
                .Where(m => m.Success)
                .Select(CanonicalPrKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Scan the output line-by-line (no whole-transcript allocation), keeping the canonical
            // base URL for each in-scope, not-yet-recorded PR.
            var added = new List<string>();
            if (planRepoNames.Count > 0)
            {
                var seen = new HashSet<string>(recordedKeys, StringComparer.OrdinalIgnoreCase);
                foreach (var line in job.OutputLines)
                    foreach (Match m in GitHubPrUrlPattern.Matches(line))
                    {
                        if (!planRepoNames.Contains(m.Groups["repo"].Value)) continue;
                        if (!seen.Add(CanonicalPrKey(m))) continue;
                        added.Add(m.Value);
                    }
            }

            // Nothing to record and no PR on the plan → leave state to whatever the agent set.
            if (plan.Prs.Count == 0 && added.Count == 0)
                return;

            var alreadyCompleted = string.Equals(plan.State, nameof(PlanStatus.Completed),
                StringComparison.OrdinalIgnoreCase);

            // Agent already recorded every PR and set Completed — no reconciliation needed.
            if (added.Count == 0 && alreadyCompleted)
                return;

            if (added.Count > 0)
            {
                foreach (var url in added)
                    plan.Prs.Add(url);
                plan.Updated = DateTime.UtcNow;
                PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcherService);
                job.EnqueueSystemOutput(
                    $"[Tendril] Recorded {added.Count} PR(s) the agent left unrecorded: {string.Join(", ", added)}");
            }

            // Route the state change through the shared path so it emits telemetry, updates the DB
            // for instant UI feedback, and invalidates the counts cache like every other case.
            if (!alreadyCompleted)
                SetPlanState(job, nameof(PlanStatus.Completed));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reconcile CreatePr result for job {JobId}", job.Id);
        }
    }

    private static string CanonicalPrKey(Match m) =>
        $"{m.Groups["owner"].Value}/{m.Groups["repo"].Value}#{m.Groups["number"].Value}".ToLowerInvariant();

    private void VerifyCreatePlanResult(JobItem job)
    {
        try
        {
            var plansDir = _planReaderService?.PlansDirectory;
            if (plansDir == null || !Directory.Exists(plansDir)) return;

            if (TryVerifyByReportedId(job, plansDir) ||
                TryVerifyByOutputRegex(job, plansDir) ||
                TryVerifyByFilesystem(job, plansDir))
            {
                MoveAttachmentsToPlanFolder(job);
                return;
            }

            if (IsDuplicatePlan(job)) return;

            MarkCreatePlanFailed(job);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify CreatePlan result for job {JobId}", job.Id);
        }
    }

    internal static string? ResolveUploadSessionId(JobArgsBase? args)
    {
        return args switch
        {
            CreatePlanArgs cp => cp.UploadSessionId,
            UpdatePlanArgs up => up.UploadSessionId,
            _ => null
        };
    }

    internal static string? ResolveAttachmentPlanFolder(JobItem job, string plansDirectory)
    {
        var folder = job.TypedArgs switch
        {
            UpdatePlanArgs u => u.FolderPath,
            _ => !string.IsNullOrEmpty(job.PlanFile) ? Path.Combine(plansDirectory, job.PlanFile) : null
        };

        return folder != null && Directory.Exists(folder) ? folder : null;
    }

    private void MoveAttachmentsToPlanFolder(JobItem job)
    {
        var sessionId = ResolveUploadSessionId(job.TypedArgs);
        if (string.IsNullOrEmpty(sessionId) || _configService == null || _planReaderService == null)
            return;

        try
        {
            var tempDir = Path.Combine(_configService.TendrilHome, "Attachments", sessionId);
            if (!Directory.Exists(tempDir)) return;

            var planFolder = ResolveAttachmentPlanFolder(job, _planReaderService.PlansDirectory);
            if (planFolder == null) return;

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
                    var content = FileHelper.ReadAllText(file);
                    var originalContent = content;

                    if (content.Contains(oldPath))
                        content = content.Replace(oldPath, newPath);

                    if (content.Contains(oldPathAlt))
                        content = content.Replace(oldPathAlt, newPathAlt);

                    if (content != originalContent)
                    {
                        FileHelper.WriteAllText(file, content);
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
            "[Tendril] WARNING: CreatePlan completed but no plan folder was found.");
        job.Status = JobStatus.Failed;
        job.StatusMessage = JobFailureAnalyzer.TryReadFailureArtifact(job.OutputLines.ToList())
            ?? job.StatusMessage
            ?? "No plan created";
    }

    internal bool IsCreatePlanSuccessful(JobItem job)
    {
        var plansDir = _planReaderService?.PlansDirectory
            ?? (_configService != null ? Path.Combine(_configService.TendrilHome, "Plans") : null);

        if (plansDir != null && Directory.Exists(plansDir))
        {
            if (TryVerifyByReportedId(job, plansDir) ||
                TryVerifyByOutputRegex(job, plansDir) ||
                TryVerifyByFilesystem(job, plansDir))
            {
                return true;
            }
        }

        return IsDuplicatePlan(job);
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

    private static bool TryVerifyByOutputRegex(JobItem job, string plansDir)
    {
        // `tendril plan create` no longer prints a `Plan created: <folder>` marker (see
        // PlanCreateCommand.Execute), but it always prints `PlanId: <id>` — resolve that ID to
        // its folder the same way TryVerifyByReportedId does, so this fallback still works
        // independently of the agent calling `tendril job status --plan-id`.
        var outputText = string.Join("\n", job.OutputLines);
        var planIdMatch = Regex.Match(outputText, @"PlanId:\s*([\w-]+)");
        if (!planIdMatch.Success) return false;

        var folder = PlanYamlHelper.FindPlanFolderById(plansDir, planIdMatch.Groups[1].Value);
        if (folder == null) return false;

        job.PlanFile = folder;
        return true;
    }

    // CreatePlan signals a deliberate duplicate rejection by ending its final message with
    // "identified as duplicate: <folder>". The negative lookahead skips the documented template
    // form, whose placeholder is angle-bracketed, so an agent that reads or quotes Program.md
    // mid-run cannot echo the marker and suppress a genuine "no plan produced" failure.
    // OutputLines holds re-serialized JSON, so the '<' arrives escaped as < — match both.
    private static bool IsDuplicatePlan(JobItem job)
    {
        var outputText = string.Join("\n", job.OutputLines);
        return Regex.IsMatch(outputText, @"identified as duplicate:\s*(?!<|\\u003[Cc])\S");
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
            if (target == PlanStatus.Blocked)
                target = PlanStatus.Draft;

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
        Action<JobItem>? persistJob = null,
        Action<string>? deleteJob = null)
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
                {
                    var remaining = waitingJob.WaitForJobIds!
                        .Where(id => jobs.TryGetValue(id, out var dep) &&
                                     dep.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked)
                        .Select(id => jobs[id])
                        .ToList();
                    if (remaining.Count > 0)
                    {
                        var waitingFor = string.Join(", ", remaining.Select(JobService.DescribeWaitDependency));
                        var newStatus = $"Waiting for {waitingFor}";
                        if (waitingJob.StatusMessage != newStatus)
                        {
                            waitingJob.StatusMessage = newStatus;
                            persistJob?.Invoke(waitingJob);
                        }
                    }
                    continue;
                }

                jobs.TryRemove(waitingJob.Id, out _);
                deleteJob?.Invoke(waitingJob.Id);
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

    internal (bool Ok, string? BlockReason) CheckDependencies(string planFolder)
        => _dependencyChecker.CheckDependencies(planFolder);

    internal void HandleRetryBlockedJobs(
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobNotification> raiseNotification,
        Func<JobArgsBase, string> startJobSkipDepCheck,
        Action<string>? deleteJob = null,
        Action<JobItem>? persistJob = null)
        => _dependencyChecker.RetryBlockedJobs(jobs, raiseNotification, startJobSkipDepCheck, deleteJob, persistJob);

    internal void WriteJobLog(JobItem job)
    {
        try
        {
            var fallback = _configService is null ? null : JobLogPaths.Log(_configService.TendrilHome, job);
            JobLogWriter.WriteLog(job, BuildPlanOutcomeSummary(job), fallback);
        }
        catch
        {
            // ignored
        }
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
