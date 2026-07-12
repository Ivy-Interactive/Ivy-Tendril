using System.Collections.Concurrent;
using System.Diagnostics;
using Ivy.Helpers;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Promptware;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Jobs;

internal record JobLaunchContext(
    JobItem Job,
    ConcurrentDictionary<string, JobItem> Jobs,
    SemaphoreSlim JobSlotSemaphore,
    TimeSpan JobTimeout,
    TimeSpan StaleOutputTimeout,
    Action<string, string, string, string, JobItem> RunHooks,
    Action<string, int?, bool, bool> CompleteJob,
    Action RaiseStructureChanged);

internal record RepoConfigEntry(
    string Path,
    string BaseBranch,
    bool ReadOnly);

internal class JobLauncher
{
    private static readonly HashSet<string> PlanWritingTypes = new(StringComparer.Ordinal)
    {
        "CreatePlan", "SplitPlan", "UpdatePlan", "ExpandPlan"
    };

    private readonly IConfigService? _configService;
    private readonly IAgentRunner? _agentRunner;
    private readonly ILogger _logger;
    private readonly string _promptsRoot;

    internal JobLauncher(IConfigService? configService, IAgentRunner? agentRunner, ILogger logger, string promptsRoot)
    {
        _configService = configService;
        _agentRunner = agentRunner;
        _logger = logger;
        _promptsRoot = promptsRoot;
    }

    internal void LaunchJob(
        JobItem job,
        ConcurrentDictionary<string, JobItem> jobs,
        SemaphoreSlim jobSlotSemaphore,
        TimeSpan jobTimeout,
        TimeSpan staleOutputTimeout,
        Action<string, string, string, string, JobItem> runHooks,
        Action<string, int?, bool, bool> completeJob,
        Action raiseStructureChanged)
    {
        var ctx = new JobLaunchContext(
            job, jobs, jobSlotSemaphore, jobTimeout, staleOutputTimeout,
            runHooks, completeJob, raiseStructureChanged);

        LaunchJob(ctx);
    }

    private void LaunchJob(JobLaunchContext ctx)
    {
        try
        {
            // Defense in depth (#1340): refuse to launch a plan job that references a repo outside its
            // project, before any state mutation. The creation/add-repo guards should prevent this, but
            // pre-existing plans may have drifted.
            if (!ValidateProjectReposOrFail(ctx))
                return;

            PrepareJobForLaunch(ctx);

            if (!ValidateJobPrerequisites(ctx, out var psi, out var stdinContent))
                return;

            var process = StartAgentProcess(ctx, psi, stdinContent);
            if (process == null)
                return;

            // Only now — after the child process genuinely exists — does the plan move to Executing.
            // Every failure path above (repo guard, hook failures, prompt/process build errors) runs
            // before this point, so a throw there never strands the plan in Executing in the first
            // place; the catch below only needs to fail the job, not revert plan state.
            TransitionPlanToExecuting(ctx);

            InitializeJobMonitoring(ctx, process);
            ctx.RaiseStructureChanged();
        }
        catch (Exception ex)
        {
            // Catch-all so a launch failure never leaks the concurrency slot (#1564) and LaunchJob
            // never propagates exceptions to its callers (neither StartJobInternal nor ProcessJobQueue
            // wrap this call). The three guards above already release the slot and return normally on
            // their anticipated failure modes — they never throw — so this only fires for genuinely
            // unhandled exceptions, including ones raised after the monitor task was already started
            // (e.g. by a RaiseStructureChanged subscriber).
            HandleUnhandledLaunchFailure(ctx, ex);
        }
    }

    private void HandleUnhandledLaunchFailure(JobLaunchContext ctx, Exception ex)
    {
        var job = ctx.Job;
        _logger.LogError(ex, "Job {JobId}: Unhandled exception during launch", job.Id);
        CrashLog.Write($"[{DateTime.UtcNow:O}] Job {job.Id}: Unhandled exception during launch: {ex}");

        // The agent process may have already started (e.g. if the failure is in TransitionPlanToExecuting,
        // which runs after StartAgentProcess succeeds) — no monitor exists yet to ever reap it, so kill it
        // here rather than leaking a real OS process.
        if (job.Process is { } process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may have already exited or never fully started — best-effort cleanup.
            }
        }

        // TryClaimCompletion is the same interlocked guard JobService.CompleteJob/StopJob use. If the
        // monitor task was already started before this exception fired (e.g. it threw from the trailing
        // RaiseStructureChanged call), the monitor may independently detect the kill above and complete
        // the job through ctx.CompleteJob concurrently. Whichever side claims completion first owns
        // marking the job terminal and releasing its slot — the loser must not touch either, or the
        // slot gets released twice.
        if (!job.TryClaimCompletion())
            return;

        job.Status = JobStatus.Failed;
        job.StatusMessage = ex.Message;
        job.CompletedAt = DateTime.UtcNow;
        ctx.JobSlotSemaphore.Release();
        ctx.RaiseStructureChanged();
    }

    // Shared by every anticipated launch-failure guard: marks the job Failed and releases the slot
    // exactly once. The catch-all in LaunchJob covers everything this doesn't.
    private static void FailJobAndReleaseSlot(JobLaunchContext ctx, string message)
    {
        var job = ctx.Job;
        if (!job.TryClaimCompletion())
            return;

        job.Status = JobStatus.Failed;
        job.StatusMessage = message;
        job.CompletedAt = DateTime.UtcNow;
        ctx.JobSlotSemaphore.Release();
        ctx.RaiseStructureChanged();
    }

    private bool ValidateProjectReposOrFail(JobLaunchContext ctx)
    {
        var job = ctx.Job;
        if (job.TypedArgs is not (ExecutePlanArgs or RetryPlanArgs or CreatePrArgs))
            return true;

        var projectConfig = _configService?.GetProject(job.Project);
        if (projectConfig == null)
            return true; // Unknown project — nothing to validate against.

        var planFolder = job.TypedArgs?.PlanFolder ?? "";
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder))
            return true;

        var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
        if (planYaml?.Repos == null || planYaml.Repos.Count == 0)
            return true;

        try
        {
            PlanProjectRepoGuard.EnsureReposBelongToProject(planYaml.Repos, projectConfig);
            return true;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError("Job {JobId}: refusing launch — {Message}", job.Id, ex.Message);
            FailJobAndReleaseSlot(ctx, ex.Message);
            return false;
        }
    }

    private void PrepareJobForLaunch(JobLaunchContext ctx)
    {
        var job = ctx.Job;
        var type = job.Type;

        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.StatusMessage = null;

        var planFolderForHooks = job.TypedArgs is not CreatePlanArgs ? (job.TypedArgs?.PlanFolder ?? "") : "";
        ctx.RunHooks("before", type, planFolderForHooks, job.Project, job);

        if (job.TypedArgs is ExecutePlanArgs or RetryPlanArgs && !string.IsNullOrEmpty(job.TypedArgs?.PlanFolder))
            EnsurePlanFolderWritable(job.TypedArgs!.PlanFolder!);

        job.SessionId = Guid.NewGuid().ToString();
    }

    // Deliberately called only after StartAgentProcess succeeds (see LaunchJob) — moved out of
    // PrepareJobForLaunch so the plan isn't flipped to Executing until an agent process genuinely
    // exists, shrinking the window in which a launch failure could strand the plan there.
    private static void TransitionPlanToExecuting(JobLaunchContext ctx)
    {
        var job = ctx.Job;
        if (job.TypedArgs is ExecutePlanArgs or RetryPlanArgs && !string.IsNullOrEmpty(job.TypedArgs?.PlanFolder))
            PlanYamlHelper.SetPlanStateByFolder(job.TypedArgs!.PlanFolder!, nameof(PlanStatus.Executing));
    }

    private bool ValidateJobPrerequisites(
        JobLaunchContext ctx,
        out ProcessStartInfo? psi,
        out string? stdinContent)
    {
        var job = ctx.Job;
        var id = job.Id;
        var type = job.Type;

        var (processInfo, stdin) = TryBuildAgentProcessStart(ctx);
        psi = processInfo;
        stdinContent = stdin;

        if (psi == null)
        {
            var programFolder = Path.Combine(_promptsRoot, type);
            _logger.LogError("Job {JobId}: No agent program found for '{Type}' in {Folder}", id, type, programFolder);
            FailJobAndReleaseSlot(ctx, $"No agent program found for '{type}'. Ensure {programFolder}/Program.md exists and config is loaded");
            return false;
        }

        return true;
    }

    private Process? StartAgentProcess(
        JobLaunchContext ctx,
        ProcessStartInfo psi,
        string? stdinContent)
    {
        var job = ctx.Job;
        var id = job.Id;

        ResolveCommandShim(psi);

        var process = new Process { StartInfo = psi };
        AttachOutputHandlers(process, job, id);

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: Failed to start process '{FileName}'", id, psi.FileName);
            var message = ex.NativeErrorCode switch
            {
                2 => $"Agent binary not found: {psi.FileName}",
                206 => $"Command line too long when launching '{psi.FileName}'",
                _ => $"Failed to start '{psi.FileName}': {ex.Message}"
            };
            FailJobAndReleaseSlot(ctx, message);
            return null;
        }

        job.Process = process;
        job.ProcessId = process.Id;

        // Start draining stdout/stderr BEFORE writing stdin. Writing a large prompt (agents receive
        // the whole compiled prompt on stdin) while nothing reads the child's output pipes is the
        // classic three-pipe deadlock, and it happens before the timeout monitor is armed — the job
        // then hangs "running…" forever with no output and no timeout ever firing (#1455).
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        WriteStdinContentAsync(process, psi, stdinContent, id);

        return process;
    }

    // Fire-and-forget so the launch thread returns immediately and the caller can arm the timeout
    // monitor without waiting for the child to consume stdin.
    private static void WriteStdinContentAsync(Process process, ProcessStartInfo psi, string? stdinContent, string id)
    {
        if (!psi.RedirectStandardInput)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (stdinContent != null)
                    await ProcessRunner.WriteStdinAndCloseAsync(process, stdinContent);
                else
                    process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Process exited before stdin could be written — safe to ignore.
            }
            catch (ObjectDisposedException)
            {
                // stdin was closed by process disposal — safe to ignore.
            }
            catch (Exception ex)
            {
                // Any other failure means the agent may have received an empty/partial prompt. Make
                // it diagnosable instead of silently faulting this detached task with no trace.
                CrashLog.Write($"[{DateTime.UtcNow:O}] Failed to write stdin for job {id}: {ex}");
            }
        });
    }

    private void InitializeJobMonitoring(JobLaunchContext ctx, Process process)
    {
        var monitor = new JobMonitor(ctx.Job.Id, ctx, process, _logger);
        monitor.Start();
    }

    private static void AttachOutputHandlers(Process process, JobItem job, string id)
    {
        process.OutputDataReceived += (_, e) =>
        {
            try
            {
                if (e.Data != null)
                {
                    job.LastOutputAt = DateTime.UtcNow;
                    if (!e.Data.Contains("\"type\":\"heartbeat\"")) job.EnqueueOutput(e.Data);
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write($"[{DateTime.UtcNow:O}] OutputDataReceived exception for job {id}: {ex}");
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            try
            {
                if (e.Data != null)
                {
                    job.EnqueueOutput($"[stderr] {e.Data}");
                    job.LastOutputAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write($"[{DateTime.UtcNow:O}] ErrorDataReceived exception for job {id}: {ex}");
            }
        };
    }

    private (ProcessStartInfo? Psi, string? StdinContent) TryBuildAgentProcessStart(JobLaunchContext ctx)
    {
        if (_configService == null || _agentRunner == null) return (null, null);

        var job = ctx.Job;
        var programFolder = Path.Combine(_promptsRoot, job.Type);
        if (!HasAgentDirectProgram(programFolder, job.Type)) return (null, null);

        var settings = _configService.Settings;
        var (values, planYaml, profileOverride) = BuildFirmwareValues(ctx, programFolder);
        values["TendrilProject"] = job.Project;

        var jobContext = BuildJobContext(job, values, programFolder);
        var resolution = AgentProviderFactory.Resolve(_agentRunner, settings, job.Type, profileOverride, jobContext);
        job.EventParser = _agentRunner.GetParser(resolution.AgentId);
        var workDir = ResolveWorkingDirectory(job, programFolder);
        job.WorkingDirectory = workDir;

        job.LogFilePath = JobLogWriter.SeedLog(_configService.TendrilHome, job);

        var customInstructions = ResolveCustomInstructions(job.Type);
        var projects = BuildProjectInfos(job);
        // Expand variables at point of use (Settings holds the raw template). normalizePaths: false —
        // the template is prose injected into the prompt, so slashes/URLs must be preserved.
        var planTemplate = PlanWritingTypes.Contains(job.Type)
            ? VariableExpansion.ExpandVariables(_configService.Settings.PlanTemplate, _configService.TendrilHome, normalizePaths: false)
            : null;
        var prompt = FirmwareCompiler.Compile(new FirmwareContext(programFolder, values, customInstructions, projects, planTemplate));
        job.CompiledPrompt = prompt;

        // Written now, not at completion, so the prompt survives a crashed or killed job. Agents that
        // cannot take the prompt on stdin are pointed at this same file.
        var promptPath = JobLogWriter.WritePrompt(_configService.TendrilHome, job);
        var promptFilePath = resolution.UsesStdinPrompt ? null : promptPath;

        var launchConfig = new AgentLaunchConfig
        {
            Prompt = prompt,
            WorkingDirectory = workDir,
            Model = string.IsNullOrEmpty(resolution.Model) ? null : resolution.Model,
            Effort = AgentProviderFactory.ParseEffort(resolution.Effort),
            SessionId = job.SessionId,
            PermissionMode = PermissionMode.FullAuto,
            AllowedTools = resolution.AllowedTools,
            WritableDirectories = ResolveWritableDirectories(job.Type, programFolder),
            ExtraArguments = resolution.ExtraArgs,
            PromptFilePath = promptFilePath,
            EnvironmentVariables = resolution.EnvironmentVariables,
        };

        job.Model = launchConfig.Model;

        var spec = resolution.Cli.BuildProcessSpec(launchConfig);
        var psi = AgentProcessHelper.ToPsi(spec);
        SetTendrilEnvironment(psi, job);
        job.CliCommand = AgentProcessHelper.FormatCliCommand(psi);

        _logger.LogInformation(
            "Job {JobId}: Agent-direct launch ({Provider}, model={Model}, effort={Effort})",
            job.Id, resolution.AgentId, resolution.Model, resolution.Effort);

        return (psi, spec.StdinContent);
    }

    private string? ResolveCustomInstructions(string promptwareName)
    {
        var settings = _configService!.Settings;
        string? instructions = null;
        if (settings.Promptwares.TryGetValue("_default", out var defaultCfg))
            instructions = defaultCfg.CustomInstructions;
        if (settings.Promptwares.TryGetValue(promptwareName, out var specificCfg)
            && !string.IsNullOrWhiteSpace(specificCfg.CustomInstructions))
            instructions = specificCfg.CustomInstructions;
        return instructions;
    }

    private static bool HasAgentDirectProgram(string programFolder, string jobType)
    {
        var programMd = Path.Combine(programFolder, "Program.md");
        if (!File.Exists(programMd)) return false;
        var scriptFile = Path.Combine(programFolder, $"{jobType}.ps1");
        return !File.Exists(scriptFile);
    }

    private (Dictionary<string, string> Values, PlanYaml? PlanYaml, string? ProfileOverride)
        BuildFirmwareValues(JobLaunchContext ctx, string programFolder)
    {
        var job = ctx.Job;
        var values = new Dictionary<string, string>
        {
            ["AgentSessionId"] = job.SessionId ?? "",
            ["TendrilJobId"] = job.Id,
            ["TendrilHome"] = _configService.TendrilHome ?? ""
        };


        if (job.TypedArgs is CreatePlanArgs)
        {
            BuildCreatePlanFirmware(ctx, values);
            return (values, null, null);
        }

        if (job.TypedArgs is SyncRepoArgs syncArgs)
        {
            values["RepoPath"] = syncArgs.RepoPath;
            values["BaseBranch"] = syncArgs.BaseBranch;
            values["UntrackedChangesPolicy"] = syncArgs.UntrackedChangesPolicy.ToString();
            return (values, null, null);
        }

        if (job.TypedArgs is UpdateMemoriesArgs updateMemoriesArgs)
        {
            values["FilesToUpdate"] = string.Join(",", updateMemoriesArgs.Files);
            return (values, null, null);
        }

        return BuildNonCreatePlanFirmware(job, values);
    }

    private void BuildCreatePlanFirmware(JobLaunchContext ctx, Dictionary<string, string> values)
    {
        var job = ctx.Job;
        var cp = job.TypedArgs as CreatePlanArgs;
        var description = cp?.Description ?? "";
        values["TaskDescription"] = description;
        values["TendrilPlansFolder"] = _configService!.PlanFolder;

        if (cp?.Force == true)
            values["Force"] = "true";
    }

    private (Dictionary<string, string> Values, PlanYaml? PlanYaml, string? ProfileOverride)
        BuildNonCreatePlanFirmware(JobItem job, Dictionary<string, string> values)
    {
        var planFolder = job.TypedArgs?.PlanFolder ?? "";

        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder))
            return (values, null, null);

        var planId = PlanYamlHelper.ExtractPlanIdFromFolder(planFolder);
        if (planId != null)
        {
            values["TendrilPlanId"] = planId;
            job.AllocatedPlanId ??= planId;
        }

        values["TendrilPlanFolder"] = planFolder;
        values["TendrilPlansFolder"] = Path.GetDirectoryName(planFolder) ?? "";

        var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
        if (planYaml == null)
            return (values, null, null);

        // Add sourceUrl to firmware header if present
        if (!string.IsNullOrEmpty(planYaml.SourceUrl))
            values["SourceUrl"] = planYaml.SourceUrl;

        if (job.TypedArgs is UpdatePlanArgs { Instructions: not null } updateArgs)
            values["UpdateInstructions"] = updateArgs.Instructions;

        if (job.TypedArgs is RetryPlanArgs retryArgs)
            values["ChangeRequest"] = retryArgs.ChangeRequest;

        var profileOverride = ExtractExecutionProfile(job, planYaml);
        AddRepoConfigsIfNeeded(job, planYaml, values);
        AddCreatePrOptions(job, values);

        return (values, planYaml, profileOverride);
    }


    private static string? ExtractExecutionProfile(JobItem job, PlanYaml planYaml)
    {
        if (job.TypedArgs is ExecutePlanArgs or RetryPlanArgs && !string.IsNullOrEmpty(planYaml.ExecutionProfile))
            return planYaml.ExecutionProfile;
        return null;
    }

    private void AddRepoConfigsIfNeeded(JobItem job, PlanYaml planYaml, Dictionary<string, string> values)
    {
        if (job.TypedArgs is not (ExecutePlanArgs or RetryPlanArgs or CreatePrArgs))
            return;

        var repoConfigs = BuildRepoConfigsYaml(planYaml, job.Project);
        if (!string.IsNullOrEmpty(repoConfigs))
            values["RepoConfigs"] = repoConfigs;
    }

    private static void AddCreatePrOptions(JobItem job, Dictionary<string, string> values)
    {
        if (job.TypedArgs is not CreatePrArgs pr)
            return;

        values["PrSolveMergeConflicts"] = pr.SolveMergeConflicts.ToString().ToLowerInvariant();
        values["PrMerge"] = pr.Merge.ToString().ToLowerInvariant();
        values["PrDeleteBranch"] = pr.DeleteBranch.ToString().ToLowerInvariant();
        values["PrIncludeArtifacts"] = pr.IncludeArtifacts.ToString().ToLowerInvariant();
        values["PrDraft"] = pr.Draft.ToString().ToLowerInvariant();
        if (!string.IsNullOrEmpty(pr.Reviewer))
            values["PrReviewer"] = pr.Reviewer;
        if (!string.IsNullOrEmpty(pr.Comment))
            values["PrComment"] = pr.Comment;
    }

    private static Dictionary<string, string> BuildJobContext(JobItem job, Dictionary<string, string> firmwareValues, string programFolder)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PROMPTWARE_DIR"] = programFolder
        };

        if (firmwareValues.TryGetValue("TendrilPlansFolder", out var plansDir))
            ctx["PLANS_DIR"] = plansDir;
        if (firmwareValues.TryGetValue("TendrilPlanFolder", out var planFolder))
            ctx["PLAN_DIR"] = planFolder;

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
            ctx["TENDRIL_HOME"] = tendrilHome;

        return ctx;
    }

    private string ResolveWorkingDirectory(JobItem job, string programFolder)
    {
        var workDir = programFolder;
        if (!string.IsNullOrEmpty(job.Project) && job.Project != "Auto")
        {
            var projectConfig = _configService?.GetProject(job.Project);
            if (projectConfig?.Repos.Count > 0)
            {
                var repoPath = Environment.ExpandEnvironmentVariables(projectConfig.Repos[0].Path);
                if (Directory.Exists(repoPath)) workDir = repoPath;
            }
        }
        return workDir;
    }

    private IReadOnlyList<string> ResolveWritableDirectories(string promptwareType, string promptwareFolder)
    {
        if (_configService == null) return [];

        var homeDir = _configService.TendrilHome;
        var homePrefix = homeDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { homeDir };

        var planFolder = _configService.PlanFolder;
        if (!planFolder.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase))
            dirs.Add(planFolder);

        var memoryDir = Path.Combine(promptwareFolder, "Memory");
        if (!memoryDir.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase))
            dirs.Add(memoryDir);

        var toolsDir = Path.Combine(promptwareFolder, "Tools");
        if (!toolsDir.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase))
            dirs.Add(toolsDir);

        if (promptwareType == Constants.JobTypes.UpdateMemories ||
            promptwareType == Constants.JobTypes.ExecutePlan ||
            promptwareType == Constants.JobTypes.RetryPlan)
        {
            foreach (var proj in _configService.Settings.Projects)
            {
                foreach (var repo in proj.Repos)
                {
                    var repoPath = Environment.ExpandEnvironmentVariables(repo.Path);
                    if (Directory.Exists(repoPath))
                        dirs.Add(repoPath);
                }
            }
        }

        return [.. dirs];
    }

    private void EnsurePlanFolderWritable(string planFolder)
    {
        var testFile = Path.Combine(planFolder, $".write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(testFile)) { }
            File.Delete(testFile);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Plan folder is not writable, attempting repair: {PlanFolder}", planFolder);
            TryRepairFolderAccess(planFolder);
        }
        finally
        {
            try { if (File.Exists(testFile)) File.Delete(testFile); } catch { /* best-effort cleanup */ }
        }
    }

    private void TryRepairFolderAccess(string planFolder)
    {
        try
        {
            string fileName;
            string arguments;
            if (OperatingSystem.IsWindows())
            {
                var username = Environment.UserName;
                fileName = "icacls";
                arguments = $"\"{planFolder}\" /grant \"{username}:(OI)(CI)M\" /T /C /Q";
            }
            else
            {
                fileName = "chmod";
                arguments = $"-R 777 \"{planFolder}\"";
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(10_000);

            if (process is { ExitCode: 0 })
                _logger.LogInformation("Repaired folder access: {PlanFolder}", planFolder);
            else
                _logger.LogWarning("Folder access repair may have failed (exit code {ExitCode}): {PlanFolder}",
                    process?.ExitCode, planFolder);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to repair access on {PlanFolder}", planFolder);
        }
    }

    private void SetTendrilEnvironment(ProcessStartInfo psi, JobItem job)
    {
        var tendrilHome = _configService!.TendrilHome;
        if (!string.IsNullOrEmpty(tendrilHome))
            psi.Environment["TENDRIL_HOME"] = tendrilHome;
        psi.Environment["TENDRIL_PLANS"] = _configService.PlanFolder;
        
        if (!string.IsNullOrEmpty(job.Project))
            psi.Environment["BW_PROJECT"] = job.Project;

        // Deliberately no TENDRIL_JOB_ID: process env does not reach the agent's nested `tendril` calls
        // (see AGENTS.md). The job id travels as the TendrilJobId firmware header and is passed as an argument.

        EnsureTendrilOnPath(psi);
    }

    internal static void EnsureTendrilOnPath(ProcessStartInfo psi)
        => AgentProcessHelper.EnsureTendrilOnPath(psi);

    internal static void ResolveCommandShim(ProcessStartInfo psi)
        => AgentProcessHelper.ResolveCommandShim(psi);

    private string? BuildRepoConfigsYaml(PlanYaml plan, string project)
    {
        if (plan.Repos.Count == 0)
            return null;

        var projectConfig = _configService?.GetProject(project);
        var planRepoNames = new HashSet<string>(
            plan.Repos.Select(r => Path.GetFileName(Environment.ExpandEnvironmentVariables(r))),
            StringComparer.OrdinalIgnoreCase);

        var lines = new List<string>();

        AddPlanRepos(plan, projectConfig, lines);
        AddBuildDependencies(projectConfig, planRepoNames, lines);

        return string.Join("\n", lines);
    }

    private void AddPlanRepos(PlanYaml plan, ProjectConfig? projectConfig, List<string> lines)
    {
        foreach (var repoPath in plan.Repos)
        {
            var expanded = Environment.ExpandEnvironmentVariables(repoPath);
            var repoRef = FindProjectRepoConfig(projectConfig, Path.GetFileName(expanded));
            var entry = new RepoConfigEntry(
                expanded,
                repoRef?.BaseBranch ?? GitHelper.ResolveDefaultBranch(expanded, _configService?.TendrilHome),
                ReadOnly: false);
            AddRepoToConfigLines(lines, entry);
        }
    }

    private void AddBuildDependencies(ProjectConfig? projectConfig, HashSet<string> planRepoNames, List<string> lines)
    {
        if (projectConfig == null)
            return;

        foreach (var depPath in projectConfig.BuildDependencies)
        {
            var expanded = Environment.ExpandEnvironmentVariables(depPath);
            var repoName = Path.GetFileName(expanded);
            if (planRepoNames.Contains(repoName))
                continue;

            var entry = new RepoConfigEntry(
                expanded,
                // Pass the raw config path: ResolveDefaultBranch owns expansion, so pre-expanding here
                // would just run env-var expansion twice on the same value.
                FindBaseBranchAcrossProjects(repoName, depPath),
                ReadOnly: true);
            AddRepoToConfigLines(lines, entry);
        }
    }

    private static void AddRepoToConfigLines(List<string> lines, RepoConfigEntry entry)
    {
        lines.Add($"- path: {entry.Path}");
        lines.Add($"  baseBranch: {entry.BaseBranch}");
        if (entry.ReadOnly)
            lines.Add("  readOnly: true");
    }

    private static RepoRef? FindProjectRepoConfig(ProjectConfig? projectConfig, string repoName)
    {
        return projectConfig?.Repos.FirstOrDefault(r =>
            Path.GetFileName(Environment.ExpandEnvironmentVariables(r.Path))
                .Equals(repoName, StringComparison.OrdinalIgnoreCase));
    }

    // repoConfigPath is the raw (unexpanded) config path; ResolveDefaultBranch expands it.
    private string FindBaseBranchAcrossProjects(string repoName, string repoConfigPath)
    {
        if (_configService == null)
            return GitHelper.ResolveDefaultBranch(repoConfigPath);

        foreach (var proj in _configService.Projects)
        {
            var repoRef = proj.Repos.FirstOrDefault(r =>
                Path.GetFileName(Environment.ExpandEnvironmentVariables(r.Path))
                    .Equals(repoName, StringComparison.OrdinalIgnoreCase));
            if (repoRef?.BaseBranch is { Length: > 0 } configured)
                return configured;
        }

        return GitHelper.ResolveDefaultBranch(repoConfigPath, _configService.TendrilHome);
    }

    private ProjectInfo[]? BuildProjectInfos(JobItem job)
    {
        if (_configService == null) return null;

        var projectNames = ProjectHelper.ParseProjects(job.Project);

        if (projectNames.Length == 0 || (projectNames.Length == 1 && projectNames[0].Equals("Auto", StringComparison.OrdinalIgnoreCase)))
            return BuildAllProjectInfos();

        var result = projectNames
            .Select(BuildSingleProjectInfo)
            .Where(p => p != null)
            .Select(p => p!)
            .ToArray();

        return result.Length > 0 ? result : null;
    }

    private ProjectInfo[] BuildAllProjectInfos()
    {
        return _configService!.Projects
            .Select(BuildProjectInfoFromConfig)
            .ToArray();
    }

    private ProjectInfo? BuildSingleProjectInfo(string name)
    {
        var config = _configService!.GetProject(name);
        return config == null ? null : BuildProjectInfoFromConfig(config);
    }

    private ProjectInfo BuildProjectInfoFromConfig(ProjectConfig config)
    {
        var repos = config.Repos.Select(r =>
        {
            var expanded = Environment.ExpandEnvironmentVariables(r.Path);
            var repoName = Path.GetFileName(expanded);
            var ownerDir = Path.GetFileName(Path.GetDirectoryName(expanded) ?? "");
            return new ProjectRepoInfo(expanded, $"{ownerDir}/{repoName}");
        }).ToList();

        var verifications = config.Verifications.Select(v =>
        {
            var delegated = _configService!.Settings.Promptwares.ContainsKey(v.Name);
            return new ProjectVerificationInfo(v.Name, v.Required, delegated);
        }).ToList();

        return new ProjectInfo(config.Name, config.Context, repos, verifications);
    }
}
