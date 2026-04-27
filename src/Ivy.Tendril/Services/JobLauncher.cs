using System.Collections.Concurrent;
using System.Diagnostics;
using Ivy.Helpers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Agents;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

internal record JobLaunchContext(
    JobItem Job,
    ConcurrentDictionary<string, JobItem> Jobs,
    SemaphoreSlim JobSlotSemaphore,
    TimeSpan JobTimeout,
    TimeSpan StaleOutputTimeout,
    Action<string, string, string, string, JobItem> RunHooks,
    Action<string, int?, bool, bool> CompleteJob,
    Action RaiseStructureChanged,
    Func<int> AllocatePlanId);

internal class JobLauncher
{
    private readonly IConfigService? _configService;
    private readonly ILogger _logger;
    private readonly string _promptsRoot;

    internal JobLauncher(IConfigService? configService, ILogger logger, string promptsRoot)
    {
        _configService = configService;
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
        Action raiseStructureChanged,
        Func<int> allocatePlanId)
    {
        var ctx = new JobLaunchContext(
            job, jobs, jobSlotSemaphore, jobTimeout, staleOutputTimeout,
            runHooks, completeJob, raiseStructureChanged, allocatePlanId);

        LaunchJob(ctx);
    }

    internal void LaunchJob(JobLaunchContext ctx)
    {
        PrepareJobForLaunch(ctx);

        if (!ValidateJobPrerequisites(ctx, out var psi, out var stdinContent))
            return;

        var process = StartAgentProcess(ctx, psi, stdinContent);
        if (process == null)
            return;

        InitializeJobMonitoring(ctx, process);
        ctx.RaiseStructureChanged();
    }

    private void PrepareJobForLaunch(JobLaunchContext ctx)
    {
        var job = ctx.Job;
        var type = job.Type;
        var args = job.Args;

        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.StatusMessage = null;

        var planFolderForHooks = type != Constants.JobTypes.CreatePlan && args.Length > 0 ? args[0] : "";
        ctx.RunHooks("before", type, planFolderForHooks, job.Project, job);

        if (type == Constants.JobTypes.ExecutePlan && args.Length > 0)
            PlanYamlHelper.SetPlanStateByFolder(args[0], "Executing");

        job.SessionId = Guid.NewGuid().ToString();
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
            job.Status = JobStatus.Failed;
            job.StatusMessage = $"No agent program found for '{type}' — ensure {programFolder}/Program.md exists and config is loaded";
            job.CompletedAt = DateTime.UtcNow;
            ctx.JobSlotSemaphore.Release();
            ctx.RaiseStructureChanged();
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
            job.Status = JobStatus.Failed;
            job.StatusMessage = $"Agent binary not found: {psi.FileName}";
            job.CompletedAt = DateTime.UtcNow;
            ctx.JobSlotSemaphore.Release();
            ctx.RaiseStructureChanged();
            return null;
        }

        WriteStdinContent(process, psi, stdinContent);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        job.Process = process;
        job.ProcessId = process.Id;

        return process;
    }

    private static void WriteStdinContent(Process process, ProcessStartInfo psi, string? stdinContent)
    {
        if (!psi.RedirectStandardInput)
            return;

        try
        {
            if (stdinContent != null)
            {
                process.StandardInput.Write(stdinContent);
                process.StandardInput.Flush();
            }
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Process exited before stdin could be written — safe to ignore.
        }
    }

    private void InitializeJobMonitoring(JobLaunchContext ctx, Process process)
    {
        var job = ctx.Job;
        var id = job.Id;

        var cts = new CancellationTokenSource(ctx.JobTimeout);
        job.TimeoutCts = cts;

        Task.Run(async () =>
        {
            _logger.LogDebug("Job {JobId}: Monitor task started", id);

            try
            {
                if (await process.WaitForExitOrKillAsync(cts.Token))
                {
                    if (ctx.Jobs.TryGetValue(id, out var j) && j.StaleOutputDetected)
                    {
                        _logger.LogInformation("Job {JobId}: Process exited, stale output detected", id);
                        ctx.CompleteJob(id, null, true, true);
                    }
                    else
                    {
                        _logger.LogInformation("Job {JobId}: Process exited with code {ExitCode}", id, process.ExitCode);
                        ctx.CompleteJob(id, process.ExitCode, false, false);
                    }
                }
                else
                {
                    _logger.LogWarning("Job {JobId}: Process killed after timeout", id);
                    ctx.CompleteJob(id, null, true, false);
                }

                _logger.LogDebug("Job {JobId}: Monitor task completed normally", id);
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Job {JobId}: Monitor task exiting (CTS disposed, job completed elsewhere)", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId}: Monitor task exception", id);
                CrashLog.Write($"[{DateTime.UtcNow:O}] JobService process monitor exception for job {id}: {ex}");
                ctx.CompleteJob(id, null, false, false);
            }
        });

        if (ctx.StaleOutputTimeout > TimeSpan.Zero)
            _ = RunStaleOutputWatchdog(id, cts, ctx.Jobs, ctx.StaleOutputTimeout);

        if (!string.IsNullOrEmpty(job.StatusFilePath))
            _ = RunStatusFilePoller(id, cts, ctx.Jobs);
    }

    internal async Task RunStaleOutputWatchdog(
        string id,
        CancellationTokenSource timeoutCts,
        ConcurrentDictionary<string, JobItem> jobs,
        TimeSpan staleOutputTimeout)
    {
        const int checkIntervalSeconds = 60;

        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                for (var i = 0; i < checkIntervalSeconds; i++)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), timeoutCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (timeoutCts.Token.IsCancellationRequested) return;
                }

                if (!jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
                    break;

                if (job.LastOutputAt.HasValue)
                {
                    var sinceLastOutput = DateTime.UtcNow - job.LastOutputAt.Value;
                    if (sinceLastOutput >= staleOutputTimeout)
                    {
                        job.StaleOutputDetected = true;
                        try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }
                        break;
                    }
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal static async Task RunStatusFilePoller(
        string id,
        CancellationTokenSource timeoutCts,
        ConcurrentDictionary<string, JobItem> jobs)
    {
        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), timeoutCts.Token);
                }
                catch (OperationCanceledException) { return; }

                if (!jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
                    break;

                if (string.IsNullOrEmpty(job.StatusFilePath)) break;

                var payload = JobStatusFile.Read(job.StatusFilePath);
                if (payload == null) continue;

                job.StatusMessage = payload.Message;
                if (!string.IsNullOrEmpty(payload.PlanId))
                    job.ReportedPlanId = payload.PlanId;
                if (!string.IsNullOrEmpty(payload.PlanTitle))
                    job.ReportedPlanTitle = payload.PlanTitle;
            }
        }
        catch (ObjectDisposedException) { }
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
        if (_configService == null) return (null, null);

        var job = ctx.Job;
        var programFolder = Path.Combine(_promptsRoot, job.Type);
        if (!HasAgentDirectProgram(programFolder, job.Type)) return (null, null);

        var settings = _configService.Settings;
        var (values, planYaml, profileOverride) = BuildFirmwareValues(ctx, programFolder);

        values["Project"] = job.Project;

        var jobContext = BuildJobContext(job, values);
        var resolution = AgentProviderFactory.Resolve(settings, job.Type, profileOverride, jobContext);
        var workDir = ResolveWorkingDirectory(job, programFolder);

        var logFile = FirmwareCompiler.GetNextLogFile(programFolder, values);
        var context = new FirmwareContext(programFolder, logFile, values);
        var prompt = FirmwareCompiler.Compile(context);

        var invocation = new AgentInvocation(
            PromptContent: prompt,
            WorkingDirectory: workDir,
            Model: resolution.Model,
            Effort: resolution.Effort,
            SessionId: job.SessionId ?? "",
            AllowedTools: resolution.AllowedTools,
            ExtraArgs: resolution.ExtraArgs);

        var psi = resolution.Provider.BuildProcessStart(invocation);
        SetTendrilEnvironment(psi, job);

        var stdinContent = resolution.Provider.UsesStdinPrompt ? prompt : null;

        _logger.LogInformation(
            "Job {JobId}: Agent-direct launch ({Provider}, model={Model}, effort={Effort})",
            job.Id, resolution.Provider.Name, resolution.Model, resolution.Effort);

        return (psi, stdinContent);
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
            ["ClaudeSessionId"] = job.SessionId ?? ""
        };

        if (job.Type == Constants.JobTypes.CreatePlan)
        {
            BuildCreatePlanFirmware(ctx, values);
            return (values, null, null);
        }

        return BuildNonCreatePlanFirmware(job, values);
    }

    private void BuildCreatePlanFirmware(JobLaunchContext ctx, Dictionary<string, string> values)
    {
        var job = ctx.Job;
        var description = PlanYamlHelper.GetNamedArg(job.Args, "-Description") ?? string.Join(" ", job.Args);
        values["Args"] = description;
        values["PlansDirectory"] = _configService!.PlanFolder;

        // Use JobService's atomic counter instead of file-based locking
        var planIdInt = ctx.AllocatePlanId();
        var planId = planIdInt.ToString("D5");
        values["PlanId"] = planId;
        job.AllocatedPlanId = planId;
    }

    private (Dictionary<string, string> Values, PlanYaml? PlanYaml, string? ProfileOverride)
        BuildNonCreatePlanFirmware(JobItem job, Dictionary<string, string> values)
    {
        var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
        values["Args"] = planFolder;

        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder))
            return (values, null, null);

        var planId = ExtractPlanIdFromFolder(planFolder);
        if (planId != null)
        {
            values["PlanId"] = planId;
            job.AllocatedPlanId ??= planId;
        }

        values["PlanFolder"] = planFolder;
        values["PlansDirectory"] = Path.GetDirectoryName(planFolder) ?? "";

        var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
        if (planYaml == null)
            return (values, null, null);

        var profileOverride = ExtractExecutionProfile(job, planYaml);
        AddRepoConfigsIfNeeded(job, planYaml, values);

        return (values, planYaml, profileOverride);
    }

    private static string? ExtractPlanIdFromFolder(string planFolder)
    {
        var folderName = Path.GetFileName(planFolder);
        var dashIdx = folderName.IndexOf('-');
        return dashIdx > 0 ? folderName[..dashIdx] : null;
    }

    private static string? ExtractExecutionProfile(JobItem job, PlanYaml planYaml)
    {
        if (job.Type == Constants.JobTypes.ExecutePlan && !string.IsNullOrEmpty(planYaml.ExecutionProfile))
            return planYaml.ExecutionProfile;
        return null;
    }

    private void AddRepoConfigsIfNeeded(JobItem job, PlanYaml planYaml, Dictionary<string, string> values)
    {
        if (job.Type is not (Constants.JobTypes.ExecutePlan or Constants.JobTypes.CreatePr))
            return;

        var repoConfigs = BuildRepoConfigsYaml(planYaml, job.Project);
        if (!string.IsNullOrEmpty(repoConfigs))
            values["RepoConfigs"] = repoConfigs;
    }

    private static Dictionary<string, string> BuildJobContext(JobItem job, Dictionary<string, string> firmwareValues)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (firmwareValues.TryGetValue("PlansDirectory", out var plansDir))
            ctx["PLANS_DIR"] = plansDir;
        if (firmwareValues.TryGetValue("PlanFolder", out var planFolder))
            ctx["PLAN_FOLDER"] = planFolder;

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

    private void SetTendrilEnvironment(ProcessStartInfo psi, JobItem job)
    {
        psi.Environment["TENDRIL_JOB_ID"] = job.Id;
        psi.Environment["TENDRIL_SESSION_ID"] = job.SessionId;
        var tendrilHome = _configService!.TendrilHome;
        if (!string.IsNullOrEmpty(tendrilHome))
            psi.Environment["TENDRIL_HOME"] = tendrilHome;
        psi.Environment["TENDRIL_CONFIG"] = _configService.ConfigPath;

        var statusFile = JobStatusFile.GetStatusFilePath(job.Id);
        psi.Environment["TENDRIL_STATUS_FILE"] = statusFile;
        job.StatusFilePath = statusFile;

        EnsureTendrilOnPath(psi);
    }

    private static void EnsureTendrilOnPath(ProcessStartInfo psi)
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) &&
            Path.GetFileNameWithoutExtension(processPath).Equals("tendril", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(processPath)!;
            PrependToPath(psi, dir);
            return;
        }

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var projectPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Ivy.Tendril.csproj"));
        if (File.Exists(projectPath))
        {
            var shimDir = Path.Combine(Path.GetTempPath(), "tendril-shim");
            var shimOutputDir = Path.Combine(Path.GetTempPath(), "tendril-shim-build");
            FileHelper.EnsureDirectory(shimDir);

            var outputArg = $"-p:BaseOutputPath=\"{shimOutputDir}{Path.DirectorySeparatorChar}\"";

            var cmdShim = Path.Combine(shimDir, "tendril.cmd");
            File.WriteAllText(cmdShim, $"@dotnet run --project \"{projectPath}\" {outputArg} -- %*\r\n");

            var bashProjectPath = projectPath.Replace('\\', '/');
            var bashOutputArg = $"-p:BaseOutputPath='{shimOutputDir.Replace('\\', '/')}/'";

            var bashShim = Path.Combine(shimDir, "tendril");
            File.WriteAllText(bashShim, $"#!/usr/bin/env bash\ndotnet run --project '{bashProjectPath}' {bashOutputArg} -- \"$@\"\n");

            PrependToPath(psi, shimDir);
        }
    }

    private static void ResolveCommandShim(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsWindows()) return;

        var fileName = psi.FileName;
        if (Path.IsPathRooted(fileName) || Path.HasExtension(fileName)) return;

        var pathDirs = (psi.Environment.TryGetValue("PATH", out var p) ? p : Environment.GetEnvironmentVariable("PATH"))
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

        foreach (var dir in pathDirs)
        {
            var cmdPath = Path.Combine(dir, fileName + ".cmd");
            if (File.Exists(cmdPath))
            {
                psi.FileName = cmdPath;
                return;
            }
        }
    }

    private static void PrependToPath(ProcessStartInfo psi, string dir)
    {
        var current = psi.Environment.TryGetValue("PATH", out var p) ? p : Environment.GetEnvironmentVariable("PATH");
        psi.Environment["PATH"] = $"{dir}{Path.PathSeparator}{current}";
    }

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
            AddRepoToConfigLines(lines, expanded,
                repoRef?.BaseBranch ?? "main",
                repoRef?.SyncStrategy ?? "fetch",
                repoRef?.PrRule ?? "default",
                false);
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

            var depBaseBranch = FindBaseBranchAcrossProjects(repoName);
            AddRepoToConfigLines(lines, expanded, depBaseBranch, "fetch", "default", true);
        }
    }

    private static void AddRepoToConfigLines(
        List<string> lines,
        string path,
        string baseBranch,
        string syncStrategy,
        string prRule,
        bool readOnly)
    {
        lines.Add($"- path: {path}");
        lines.Add($"  baseBranch: {baseBranch}");
        lines.Add($"  syncStrategy: {syncStrategy}");
        lines.Add($"  prRule: {prRule}");
        if (readOnly)
            lines.Add("  readOnly: true");
    }

    private static RepoRef? FindProjectRepoConfig(ProjectConfig? projectConfig, string repoName)
    {
        return projectConfig?.Repos.FirstOrDefault(r =>
            Path.GetFileName(Environment.ExpandEnvironmentVariables(r.Path))
                .Equals(repoName, StringComparison.OrdinalIgnoreCase));
    }

    private string FindBaseBranchAcrossProjects(string repoName)
    {
        if (_configService == null) return "main";

        foreach (var proj in _configService.Projects)
        {
            var repoRef = proj.Repos.FirstOrDefault(r =>
                Path.GetFileName(Environment.ExpandEnvironmentVariables(r.Path))
                    .Equals(repoName, StringComparison.OrdinalIgnoreCase));
            if (repoRef?.BaseBranch is { Length: > 0 } configured)
                return configured;
        }

        return "main";
    }
}
