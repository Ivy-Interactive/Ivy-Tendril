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

        if (job.Type is Constants.JobTypes.ExecutePlan or Constants.JobTypes.CreatePr)
            RetryBlockedJobs(jobs, raiseNotification, startJobSkipDepCheck);

        if (isSuccess && job.Type is Constants.JobTypes.ExecutePlan or Constants.JobTypes.CreatePr or Constants.JobTypes.CreateIssue)
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
        else if (isSuccess && job.Type == Constants.JobTypes.ExecutePlan)
        {
            SyncPlanArtifacts(job);
            EnsurePlanStateTransitioned(job);
        }
        else if (isSuccess && job.Type == Constants.JobTypes.CreateIssue)
        {
            SetPlanState(job, "Completed");
        }
        else if (isSuccess && job.Type is Constants.JobTypes.UpdatePlan or Constants.JobTypes.ExpandPlan)
        {
            SetPlanState(job, "Draft");
        }
        else if (isSuccess && job.Type == Constants.JobTypes.SplitPlan)
        {
            SetPlanState(job, "Skipped");
        }
        else if (isSuccess && job.Type == Constants.JobTypes.CreatePlan)
        {
            VerifyCreatePlanResult(job);
        }
    }

    private void TrackTelemetry(JobItem job, bool isSuccess)
    {
        if (isSuccess && job.Type == Constants.JobTypes.CreatePlan && job.Status == JobStatus.Completed)
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

        if (isSuccess && job.Type == Constants.JobTypes.CreatePr)
        {
            _telemetryService?.TrackPrCreated(new PrCreatedContext(job.DurationSeconds));
        }

        _telemetryService?.TrackJobCompleted(job.Type, job.Status, job.DurationSeconds);

        if (_telemetryService != null)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _telemetryService.FlushAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to flush telemetry (best-effort)");
                }
            });
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

    private void SyncPlanArtifacts(JobItem job)
    {
        var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder)) return;

        try
        {
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            var changed = false;

            changed |= SyncVerificationsFromReports(planFolder, plan);
            changed |= SyncCommitsFromWorktrees(planFolder, plan);

            if (changed)
            {
                plan.Updated = DateTime.UtcNow;
                PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcherService);
                _logger.LogInformation(
                    "Synced plan artifacts from disk for {PlanFolder} (agent did not call CLI commands)",
                    Path.GetFileName(planFolder));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync plan artifacts for {PlanFolder}", planFolder);
        }
    }

    private bool SyncVerificationsFromReports(string planFolder, PlanYaml plan)
    {
        var verificationDir = Path.Combine(planFolder, "verification");
        if (!Directory.Exists(verificationDir)) return false;
        if (plan.Verifications == null || plan.Verifications.Count == 0) return false;

        var changed = false;

        foreach (var reportFile in Directory.GetFiles(verificationDir, "*.md"))
        {
            var reportName = Path.GetFileNameWithoutExtension(reportFile);
            if (reportName.Equals("PreExecution", StringComparison.OrdinalIgnoreCase)) continue;

            var verification = plan.Verifications.FirstOrDefault(v =>
                v.Name.Equals(reportName, StringComparison.OrdinalIgnoreCase));
            if (verification == null) continue;
            if (verification.Status != "Pending") continue;

            try
            {
                var content = FileHelper.ReadAllText(reportFile);
                var result = PlanYamlHelper.ParseVerificationResultFromReport(content);
                if (result != null)
                {
                    verification.Status = result;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read verification report {ReportPath}", reportFile);
                // Skip unreadable report files
            }
        }

        return changed;
    }

    private bool SyncCommitsFromWorktrees(string planFolder, PlanYaml plan)
    {
        if (plan.Commits.Count > 0) return false;

        var worktreesDir = Path.Combine(planFolder, "worktrees");
        if (!Directory.Exists(worktreesDir)) return false;

        var planId = PlanYamlHelper.ExtractPlanIdFromFolder(planFolder);
        var safeTitle = PlanYamlHelper.ExtractSafeTitleFromFolder(planFolder);
        if (planId == null || safeTitle == null) return false;

        var branchName = $"tendril/{planId}-{safeTitle}";
        var planRepoNames = new HashSet<string>(
            plan.Repos.Select(r => Path.GetFileName(Environment.ExpandEnvironmentVariables(r))),
            StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var wtDir in IterateWorktrees(worktreesDir, planRepoNames))
        {
            var commits = ExtractCommitsFromWorktree(wtDir, branchName, plan);
            changed |= AddCommitsToPlan(plan, commits);
        }

        return changed;
    }

    private IEnumerable<string> IterateWorktrees(string worktreesDir, HashSet<string> planRepoNames)
    {
        foreach (var wtDir in Directory.GetDirectories(worktreesDir))
        {
            var wtName = Path.GetFileName(wtDir);
            if (planRepoNames.Count > 0 && !planRepoNames.Contains(wtName))
                continue;

            var gitFile = Path.Combine(wtDir, ".git");
            if (File.Exists(gitFile))
                yield return wtDir;
        }
    }

    private List<string> ExtractCommitsFromWorktree(string wtDir, string branchName, PlanYaml plan)
    {
        var commits = new List<string>();
        try
        {
            var gitFile = Path.Combine(wtDir, ".git");
            var gitContent = FileHelper.ReadAllText(gitFile).Trim();
            var gitDirMatch = Regex.Match(gitContent, @"gitdir:\s*(.+)");
            if (!gitDirMatch.Success) return commits;

            var gitDir = gitDirMatch.Groups[1].Value.Trim();
            var repoGitDir = Path.GetFullPath(Path.Combine(gitDir, "..", ".."));
            var repoRoot = Path.GetDirectoryName(repoGitDir);
            if (repoRoot == null || !Directory.Exists(repoRoot)) return commits;

            var baseBranch = ResolveBaseBranch(repoRoot, plan);

            var psi = new ProcessStartInfo("git",
                $"log --format=%H \"{baseBranch}..{branchName}\"")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return commits;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExitOrKill(10000);
            if (process.ExitCode != 0 || string.IsNullOrEmpty(output)) return commits;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var hash = line.Trim();
                if (hash.Length >= 7)
                {
                    var shortHash = hash.Length > 9 ? hash[..9] : hash;
                    commits.Add(shortHash);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read commits from worktree {Worktree}", wtDir);
            // Skip worktrees that can't be read
        }

        return commits;
    }

    private static bool AddCommitsToPlan(PlanYaml plan, IEnumerable<string> commits)
    {
        var changed = false;
        foreach (var commit in commits)
        {
            if (!plan.Commits.Contains(commit))
            {
                plan.Commits.Add(commit);
                changed = true;
            }
        }
        return changed;
    }

    private string ResolveBaseBranch(string repoRoot, PlanYaml plan)
    {
        var configured = TryGetConfiguredBaseBranch(repoRoot, plan);
        return configured ?? DetectBaseBranch(repoRoot);
    }

    private string? TryGetConfiguredBaseBranch(string repoRoot, PlanYaml plan)
    {
        if (_configService == null) return null;

        if (!string.IsNullOrEmpty(plan.Project))
        {
            var project = _configService.GetProject(plan.Project);
            var repoRef = project?.Repos.FirstOrDefault(r =>
                Path.GetFullPath(r.Path).Equals(Path.GetFullPath(repoRoot), StringComparison.OrdinalIgnoreCase));
            if (repoRef?.BaseBranch is { Length: > 0 } configured)
                return $"origin/{configured}";
        }

        foreach (var proj in _configService.Projects)
        {
            var repoRef = proj.Repos.FirstOrDefault(r =>
                Path.GetFullPath(r.Path).Equals(Path.GetFullPath(repoRoot), StringComparison.OrdinalIgnoreCase));
            if (repoRef?.BaseBranch is { Length: > 0 } found)
                return $"origin/{found}";
        }

        return null;
    }

    private static string DetectBaseBranch(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("git",
                "symbolic-ref refs/remotes/origin/HEAD")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return "origin/main";
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExitOrKill(5000);

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                return output.Replace("refs/remotes/", "");
        }
        catch
        {
            // Fall through to default
        }

        return "origin/main";
    }

    private void EnsurePlanStateTransitioned(JobItem job)
    {
        try
        {
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
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
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";

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
            if (_planReaderService == null) return;
            var plansDir = _planReaderService.PlansDirectory;
            if (!Directory.Exists(plansDir)) return;

            // 1. Agent reported a plan ID via the status API
            if (!string.IsNullOrEmpty(job.ReportedPlanId))
            {
                var reportedFolder = PlanYamlHelper.FindPlanFolderById(plansDir, job.ReportedPlanId);
                if (reportedFolder != null)
                {
                    job.PlanFile = reportedFolder;
                    return;
                }

                // Reported ID doesn't match any plan folder — clear it so the
                // bogus value doesn't leak into the UI (e.g. agent copied the
                // example "01234" from documentation instead of calling the CLI).
                job.ReportedPlanId = null;
                job.ReportedPlanTitle = null;
            }

            var planId = job.ReportedPlanId ?? job.AllocatedPlanId;

            // 2. Check output regex (backward compat)
            var outputText = string.Join("\n", job.OutputLines);
            var createdMatch = Regex.Match(outputText, @"Plan created:\s*(\S+)");
            if (createdMatch.Success)
            {
                job.PlanFile = createdMatch.Groups[1].Value;
                return;
            }

            // 3. Duplicate detection
            var duplicate = Regex.IsMatch(outputText, "identified as duplicate:");
            if (duplicate) return;

            // 4. Filesystem fallback using allocated or reported ID
            var planFolder = PlanYamlHelper.FindPlanFolderById(plansDir, planId);
            if (planFolder != null)
            {
                job.PlanFile = planFolder;
                return;
            }

            // 5. Check trash
            var trashDir = _configService != null
                ? Path.Combine(_configService.TendrilHome, "Trash")
                : null;
            var trashEntry = trashDir != null && !string.IsNullOrEmpty(planId)
                ? PlanYamlHelper.FindTrashEntryById(trashDir, planId)
                : null;
            if (trashEntry != null) return;

            // 6. Nothing found — mark as failed
            job.EnqueueOutput(
                "[Tendril] WARNING: CreatePlan completed but no plan folder or trash entry was found.");
            job.Status = JobStatus.Failed;

            var failureMessage = JobFailureAnalyzer.TryReadFailureArtifact(job.OutputLines.ToList());
            job.StatusMessage = failureMessage ?? "No plan created";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify CreatePlan result for job {JobId}", job.Id);
        }
    }

    internal void ResetPlanState(JobItem job)
    {
        try
        {
            if (job.Type is Constants.JobTypes.CreatePlan or Constants.JobTypes.CreatePr or Constants.JobTypes.CreateIssue) return;

            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            var newState = job.Type == Constants.JobTypes.ExecutePlan ? "Failed" : "Draft";
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
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
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

    private void ScheduleWorktreeCleanup(JobItem job)
    {
        if (job.Type != Constants.JobTypes.ExecutePlan) return;

        var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder)) return;

        var worktreesDir = Path.Combine(planFolder, "worktrees");
        if (!Directory.Exists(worktreesDir)) return;

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

    internal void HandleRetryBlockedJobs(
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobNotification> raiseNotification,
        Func<string, string[], string> startJobSkipDepCheck)
        => RetryBlockedJobs(jobs, raiseNotification, startJobSkipDepCheck);

    private void RetryBlockedJobs(
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobNotification> raiseNotification,
        Func<string, string[], string> startJobSkipDepCheck)
    {
        var blockedJobs = jobs.Values
            .Where(j => j.Status == JobStatus.Blocked && j.Type == Constants.JobTypes.ExecutePlan)
            .ToList();

        foreach (var blockedJob in blockedJobs)
        {
            var planFolder = blockedJob.Args.Length > 0 ? blockedJob.Args[0] : "";
            if (string.IsNullOrEmpty(planFolder)) continue;

            var (ok, _) = CheckDependencies(planFolder);
            if (!ok) continue;

            if (HasActiveJobForPlan(planFolder, jobs)) continue;

            if (!jobs.TryRemove(blockedJob.Id, out _)) continue;

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
                    j.Type == Constants.JobTypes.ExecutePlan &&
                    j.Status is JobStatus.Blocked or JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
                    j.Args.Length > 0 &&
                    j.Args[0].Equals(dir, StringComparison.OrdinalIgnoreCase));
                if (hasExistingJob) continue;

                var (allMet, _) = CheckDependencies(dir);
                if (allMet)
                {
                    PlanYamlHelper.SetPlanStateByFolder(dir, "Building");
                    startJobSkipDepCheck(Constants.JobTypes.ExecutePlan, new[] { dir });
                }
            }
        }
        catch
        {
        }
    }

    private static bool HasActiveJobForPlan(string planFolder, ConcurrentDictionary<string, JobItem> jobs)
    {
        var planRepos = PlanYamlHelper.ReadPlanYaml(planFolder)?.Repos;

        return jobs.Values.Any(j =>
        {
            if (j.Type != Constants.JobTypes.ExecutePlan) return false;
            if (j.Status is not (JobStatus.Running or JobStatus.Queued or JobStatus.Pending)) return false;
            if (j.Args.Length == 0) return false;

            var otherFolder = j.Args[0];
            if (otherFolder.Equals(planFolder, StringComparison.OrdinalIgnoreCase))
                return true;

            if (planRepos is { Count: > 0 })
            {
                var otherRepos = PlanYamlHelper.ReadPlanYaml(otherFolder)?.Repos;
                if (otherRepos != null && planRepos.Any(r => otherRepos.Contains(r, StringComparer.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        });
    }

    private string? ResolvePlanFolder(JobItem job)
    {
        if (job.Type != Constants.JobTypes.CreatePlan)
            return job.Args.Length > 0 ? job.Args[0] : null;

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
            WriteRawOutputLog(job);
        }
        catch
        {
        }

        try
        {
            MoveStatusFileIfNeeded(job);
        }
        catch { }

        if (_planReaderService == null || string.IsNullOrEmpty(job.PlanFile))
            return;

        if (job.Type == Constants.JobTypes.CreatePlan)
            return;

        try
        {
            var logContent = BuildJobLogContent(job);
            _planReaderService.AddLog(job.PlanFile, job.Type, logContent);
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
            JobStatusFile.MoveLogToPlanFolder(job.StatusFilePath, planFolder, job.Type);
    }

    private string BuildJobLogContent(JobItem job)
    {
        var duration = job.DurationSeconds.HasValue ? $"{job.DurationSeconds}s" : "unknown";
        var logContent = $"# {job.Type}\n\n" +
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

    private static string BuildPlanOutcomeSummary(JobItem job)
    {
        if (job.Type != Constants.JobTypes.ExecutePlan)
            return "";

        var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
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
