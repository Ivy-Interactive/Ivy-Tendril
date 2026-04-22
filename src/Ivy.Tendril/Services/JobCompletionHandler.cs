using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
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
    }

    internal void HandleCompletion(
        JobItem job,
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobItem> persistJob,
        Action<JobNotification> raiseNotification,
        Action raisePropertyChanged,
        Func<string, string[], string> startJobSkipDepCheck)
    {
        var isSuccess = job.Status == JobStatus.Completed;

        RunAfterHooks(job);
        SendCompletionNotification(job, isSuccess, raiseNotification);
        HandlePlanStateTransition(job, isSuccess);
        TrackTelemetry(job, isSuccess);
        CleanupInboxFile(job);
        WriteJobLog(job);
        NotifyPlanWatcher(job);
        ScheduleCostCalculation(job, jobs, persistJob, raisePropertyChanged);

        if (job.Status is JobStatus.Failed or JobStatus.Timeout)
            ScheduleWorktreeCleanup(job);

        if (isSuccess && job.Type is "ExecutePlan" or "CreatePr")
            RetryBlockedJobs(jobs, raiseNotification, startJobSkipDepCheck);

        if (isSuccess && job.Type is "ExecutePlan" or "CreatePr" or "CreateIssue")
        {
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            RetryBlockedDependents(planFolder, jobs, startJobSkipDepCheck);
        }
    }

    private void RunAfterHooks(JobItem job)
    {
        var planFolderForHooks = job.Args.Length > 0 ? job.Args[0] : "";
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
        }
        else if (isSuccess && job.Type == "ExecutePlan")
        {
            EnsurePlanStateTransitioned(job);
        }
        else if (isSuccess && job.Type == "CreateIssue")
        {
            SetPlanState(job, "Completed");
        }
        else if (isSuccess && job.Type is "UpdatePlan" or "ExpandPlan" or "SplitPlan")
        {
            SetPlanState(job, "Draft");
        }
        else if (isSuccess && job.Type == "CreatePlan")
        {
            VerifyCreatePlanResult(job);
        }
    }

    private void TrackTelemetry(JobItem job, bool isSuccess)
    {
        if (isSuccess && job.Type == "CreatePlan" && job.Status == JobStatus.Completed)
        {
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            var level = "NiceToHave";
            if (Directory.Exists(planFolder))
            {
                var plan = PlanYamlHelper.ReadPlanYaml(planFolder);
                if (plan != null) level = plan.Level;
            }

            _telemetryService?.TrackPlanCreated(new PlanCreatedContext(level, job.DurationSeconds));
        }

        if (isSuccess && job.Type == "CreatePr")
        {
            _telemetryService?.TrackPrCreated(new PrCreatedContext(job.DurationSeconds));
        }

        _telemetryService?.TrackJobCompleted(job.Type, job.Status, job.DurationSeconds);

        if (_telemetryService != null)
            _ = Task.Run(async () => { try { await _telemetryService.FlushAsync(); } catch { /* best-effort */ } });
    }

    private void NotifyPlanWatcher(JobItem job)
    {
        var notifyFolder = job.Args.Length > 0 ? job.Args[0] : null;
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
        var jobArgs = job.Args;
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

                    if (jobArgs.Length > 0)
                        PlanYamlHelper.LogCostToCsv(jobArgs[0], jobType, costCalc.TotalTokens, costCalc.TotalCost);
                }
            }
            catch
            {
                /* Best-effort cost tracking */
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
            try
            {
                if (!string.IsNullOrWhiteSpace(hook.Condition))
                {
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

                    if (condProc?.ExitCode != 0 ||
                        condOutput.Equals("False", StringComparison.OrdinalIgnoreCase))
                    {
                        job.EnqueueOutput($"[hook:{hook.Name}] Condition not met, skipping");
                        continue;
                    }
                }

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
                actionPsi.Environment["TENDRIL_CONFIG"] = _configService.ConfigPath;

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
            catch (Exception ex)
            {
                job.EnqueueOutput($"[hook:{hook.Name}] Error: {ex.Message}");
            }
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
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
            if (planYaml == null) return;

            if (planYaml.State is "Executing" or "Building")
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
        catch
        {
        }
    }

    private void SetPlanState(JobItem job, string state)
    {
        try
        {
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            if (_planReaderService != null && Enum.TryParse<PlanStatus>(state, true, out var status))
                _planReaderService.TransitionState(Path.GetFileName(planFolder), status);
            else
                PlanYamlHelper.SetPlanStateByFolder(planFolder, state);
        }
        catch
        {
        }
    }

    private void VerifyCreatePlanResult(JobItem job)
    {
        try
        {
            if (_planReaderService == null) return;
            var plansDir = _planReaderService.PlansDirectory;
            if (!Directory.Exists(plansDir)) return;

            var outputText = string.Join("\n", job.OutputLines);
            var createdMatch = Regex.Match(outputText, @"Plan created:\s*(\S+)");
            var duplicate = Regex.IsMatch(outputText, "identified as duplicate:");

            if (createdMatch.Success)
            {
                job.PlanFile = createdMatch.Groups[1].Value;
            }
            else if (!duplicate)
            {
                var planFolder = PlanYamlHelper.FindPlanFolderById(plansDir, job.AllocatedPlanId);
                if (planFolder != null)
                {
                    job.PlanFile = planFolder;
                }
                else
                {
                    var trashDir = _configService != null
                        ? Path.Combine(_configService.TendrilHome, "Trash")
                        : null;
                    var trashEntry = trashDir != null && !string.IsNullOrEmpty(job.AllocatedPlanId)
                        ? PlanYamlHelper.FindTrashEntryById(trashDir, job.AllocatedPlanId)
                        : null;

                    if (trashEntry == null)
                    {
                        job.EnqueueOutput(
                            "[Tendril] WARNING: CreatePlan completed but no plan folder or trash entry was found.");
                        job.Status = JobStatus.Failed;

                        var failureMessage = JobFailureAnalyzer.TryReadFailureArtifact(job.OutputLines.ToList());
                        job.StatusMessage = failureMessage ?? "No plan created";
                    }
                }
            }
        }
        catch
        {
        }
    }

    internal void ResetPlanState(JobItem job)
    {
        try
        {
            if (job.Type is "CreatePlan" or "CreatePr" or "CreateIssue") return;

            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            var newState = job.Type == "ExecutePlan" ? "Failed" : "Draft";
            PlanYamlHelper.SetPlanStateByFolder(planFolder, newState);
        }
        catch
        {
        }
    }

    internal void ResetPlanStateToBlocked(JobItem job)
    {
        try
        {
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            PlanYamlHelper.SetPlanStateByFolder(planFolder, "Blocked");
        }
        catch
        {
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

    private void ScheduleWorktreeCleanup(JobItem job)
    {
        if (job.Type != "ExecutePlan") return;

        var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder)) return;

        var worktreesDir = Path.Combine(planFolder, "worktrees");
        if (!Directory.Exists(worktreesDir)) return;

        var lifecycleLogger = _worktreeLifecycleLogger;

        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            try
            {
                PlanReaderService.RemoveWorktrees(planFolder, lifecycleLogger: lifecycleLogger);

                if (Directory.Exists(worktreesDir) && Directory.GetDirectories(worktreesDir).Length == 0)
                    Directory.Delete(worktreesDir, false);
            }
            catch
            {
            }
        });
    }

    internal (bool Ok, string? BlockReason) CheckDependencies(string planFolder)
    {
        try
        {
            var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
            if (planYaml?.DependsOn == null || planYaml.DependsOn.Count == 0)
                return (true, null);

            var plansDir = _planReaderService?.PlansDirectory;
            if (plansDir == null) return (true, null);

            foreach (var dep in planYaml.DependsOn)
            {
                var depFolder = Path.Combine(plansDir, dep);
                var depPlan = PlanYamlHelper.ReadPlanYaml(depFolder);

                if (depPlan == null)
                    return (false, $"Dependency '{dep}' not found");

                if (!depPlan.State.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                    return (false, $"Dependency '{dep}' is '{depPlan.State}', not Completed");

                if (depPlan.Prs.Count == 0)
                    continue;

                foreach (var prUrl in depPlan.Prs.Where(PullRequestApp.IsValidUrl))
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "gh",
                            Arguments = $"pr view \"{prUrl}\" --json state -q .state",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        var output = proc?.StandardOutput.ReadToEnd().Trim() ?? "";
                        proc.WaitForExitOrKill(10000);

                        if (!output.Equals("MERGED", StringComparison.OrdinalIgnoreCase))
                            return (false, $"Dependency '{dep}' PR {prUrl} is '{output}', not MERGED");
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Failed to check PR status for '{dep}': {ex.Message}");
                    }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Dependency check failed: {ex.Message}");
        }
    }

    private void RetryBlockedJobs(
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobNotification> raiseNotification,
        Func<string, string[], string> startJobSkipDepCheck)
    {
        var blockedJobs = jobs.Values
            .Where(j => j.Status == JobStatus.Blocked && j.Type == "ExecutePlan")
            .ToList();

        foreach (var blockedJob in blockedJobs)
        {
            var planFolder = blockedJob.Args.Length > 0 ? blockedJob.Args[0] : "";
            if (string.IsNullOrEmpty(planFolder)) continue;

            var (ok, _) = CheckDependencies(planFolder);
            if (!ok) continue;

            if (!jobs.TryRemove(blockedJob.Id, out _)) continue;

            if (HasActiveJobForPlan(planFolder, jobs)) continue;

            PlanYamlHelper.SetPlanStateByFolder(planFolder, "Building");
            startJobSkipDepCheck(blockedJob.Type, blockedJob.Args);

            raiseNotification(new JobNotification(
                "Job Unblocked",
                $"{blockedJob.PlanFile}: dependencies now satisfied, auto-restarting",
                true));
        }
    }

    private void RetryBlockedDependents(
        string completedPlanFolder,
        ConcurrentDictionary<string, JobItem> jobs,
        Func<string, string[], string> startJobSkipDepCheck)
    {
        try
        {
            var completedFolderName = Path.GetFileName(completedPlanFolder);
            var plansDir = _planReaderService?.PlansDirectory;
            if (string.IsNullOrEmpty(plansDir) || !Directory.Exists(plansDir)) return;

            foreach (var dir in Directory.GetDirectories(plansDir))
            {
                var planYaml = PlanYamlHelper.ReadPlanYaml(dir);
                if (planYaml == null) continue;
                if (!planYaml.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase)) continue;
                if (!planYaml.DependsOn.Contains(completedFolderName, StringComparer.OrdinalIgnoreCase)) continue;

                var hasExistingJob = jobs.Values.Any(j =>
                    j.Type == "ExecutePlan" &&
                    j.Status is JobStatus.Blocked or JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
                    j.Args.Length > 0 &&
                    j.Args[0].Equals(dir, StringComparison.OrdinalIgnoreCase));
                if (hasExistingJob) continue;

                var (allMet, _) = CheckDependencies(dir);
                if (allMet)
                {
                    PlanYamlHelper.SetPlanStateByFolder(dir, "Building");
                    startJobSkipDepCheck("ExecutePlan", new[] { dir });
                }
            }
        }
        catch
        {
        }
    }

    private static bool HasActiveJobForPlan(string planFolder, ConcurrentDictionary<string, JobItem> jobs)
    {
        return jobs.Values.Any(j =>
            j.Type == "ExecutePlan" &&
            j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
            j.Args.Length > 0 &&
            j.Args[0].Equals(planFolder, StringComparison.OrdinalIgnoreCase));
    }

    internal void WriteJobLog(JobItem job)
    {
        try
        {
            WriteRawOutputLog(job);
        }
        catch
        {
        }

        if (_planReaderService == null || string.IsNullOrEmpty(job.PlanFile))
            return;

        if (job.Type == "CreatePlan")
            return;

        try
        {
            var duration = job.DurationSeconds.HasValue ? $"{job.DurationSeconds}s" : "unknown";
            var logContent = $"# {job.Type}\n\n" +
                             $"- **Status:** {job.Status}\n" +
                             $"- **Started:** {job.StartedAt:u}\n" +
                             $"- **Completed:** {job.CompletedAt:u}\n" +
                             $"- **Duration:** {duration}\n";

            if (!string.IsNullOrEmpty(job.SessionId))
                logContent += $"- **SessionId:** {job.SessionId}\n";

            if (job.Status == JobStatus.Timeout && job.StatusMessage != null)
                logContent += $"- **Timeout Reason:** {job.StatusMessage}\n";

            _planReaderService.AddLog(job.PlanFile, job.Type, logContent);

            if (job.Status is JobStatus.Failed or JobStatus.Timeout && job.OutputLines.Count > 0)
            {
                var planFolder = job.Args.Length > 0 ? job.Args[0] : null;

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
        }
        catch
        {
        }
    }

    private void WriteRawOutputLog(JobItem job)
    {
        if (job.OutputLines.Count == 0) return;

        var logsDir = Path.Combine(_promptsRoot, job.Type, "Logs");
        if (!Directory.Exists(logsDir)) return;

        var logName = !string.IsNullOrEmpty(job.AllocatedPlanId)
            ? job.AllocatedPlanId
            : job.Id;

        var rawFile = Path.Combine(logsDir, $"{logName}.raw.jsonl");
        File.WriteAllLines(rawFile, job.OutputLines);
    }
}
