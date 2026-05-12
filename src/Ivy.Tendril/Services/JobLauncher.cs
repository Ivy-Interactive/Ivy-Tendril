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
    Action RaiseStructureChanged);

internal record RepoConfigEntry(
    string Path,
    string BaseBranch,
    string SyncStrategy,
    string PrRule,
    bool ReadOnly);

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
        Action raiseStructureChanged)
    {
        var ctx = new JobLaunchContext(
            job, jobs, jobSlotSemaphore, jobTimeout, staleOutputTimeout,
            runHooks, completeJob, raiseStructureChanged);

        LaunchJob(ctx);
    }

    private void LaunchJob(JobLaunchContext ctx)
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

        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.StatusMessage = null;

        var planFolderForHooks = job.TypedArgs is not CreatePlanArgs ? (job.TypedArgs?.PlanFolder ?? "") : "";
        ctx.RunHooks("before", type, planFolderForHooks, job.Project, job);

        if (job.TypedArgs is ExecutePlanArgs && !string.IsNullOrEmpty(job.TypedArgs?.PlanFolder))
            PlanYamlHelper.SetPlanStateByFolder(job.TypedArgs!.PlanFolder!, "Executing");

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
        if (_configService == null) return (null, null);

        var job = ctx.Job;
        var programFolder = Path.Combine(_promptsRoot, job.Type);
        if (!HasAgentDirectProgram(programFolder, job.Type)) return (null, null);

        var settings = _configService.Settings;
        var (values, planYaml, profileOverride) = BuildFirmwareValues(ctx, programFolder);
        values["TendrilProject"] = job.Project;

        var jobContext = BuildJobContext(job, values, programFolder);
        var resolution = AgentProviderFactory.Resolve(settings, job.Type, profileOverride, jobContext);
        var workDir = ResolveWorkingDirectory(job, programFolder);

        var logFile = FirmwareCompiler.GetNextLogFile(programFolder);
        job.LogFilePath = logFile;

        var customInstructions = ResolveCustomInstructions(job.Type);
        var prompt = FirmwareCompiler.Compile(new FirmwareContext(programFolder, values, customInstructions));
        job.CompiledPrompt = prompt;

        var promptFilePath = WritePromptFileIfNeeded(resolution, prompt, job.Id, values);

        var invocation = new AgentInvocation(
            PromptContent: prompt,
            WorkingDirectory: workDir,
            Model: resolution.Model,
            Effort: resolution.Effort,
            SessionId: job.SessionId ?? "",
            AllowedTools: resolution.AllowedTools,
            ExtraArgs: resolution.ExtraArgs,
            PromptFilePath: promptFilePath);

        var psi = resolution.Provider.BuildProcessStart(invocation);
        SetTendrilEnvironment(psi, job);
        job.CliCommand = AgentProcessHelper.FormatCliCommand(psi);

        _logger.LogInformation(
            "Job {JobId}: Agent-direct launch ({Provider}, model={Model}, effort={Effort})",
            job.Id, resolution.Provider.Name, resolution.Model, resolution.Effort);

        return (psi, resolution.Provider.UsesStdinPrompt ? prompt : null);
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

    private static string? WritePromptFileIfNeeded(
        AgentResolution resolution, string prompt, string jobId, Dictionary<string, string> values)
    {
        if (resolution.Provider.UsesStdinPrompt)
            return null;

        var tempDir = values.TryGetValue("TendrilPlanFolder", out var pf)
            ? Path.Combine(pf, "temp")
            : Path.GetTempPath();
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, $"prompt-{jobId}.md");
        File.WriteAllText(path, prompt);
        return path;
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
            ["TendrilConfigPath"] = _configService!.ConfigPath,
            ["TendrilHome"] = _configService.TendrilHome ?? ""
        };

        if (job.TypedArgs is CreatePlanArgs)
        {
            BuildCreatePlanFirmware(ctx, values);
            return (values, null, null);
        }

        return BuildNonCreatePlanFirmware(job, values);
    }

    private void BuildCreatePlanFirmware(JobLaunchContext ctx, Dictionary<string, string> values)
    {
        var job = ctx.Job;
        var cp = job.TypedArgs as CreatePlanArgs;
        var description = cp?.Description ?? "";
        values["Args"] = description;
        values["TendrilPlansFolder"] = _configService!.PlanFolder;

        if (cp?.Force == true)
            values["Force"] = "true";
    }

    private (Dictionary<string, string> Values, PlanYaml? PlanYaml, string? ProfileOverride)
        BuildNonCreatePlanFirmware(JobItem job, Dictionary<string, string> values)
    {
        var planFolder = job.TypedArgs?.PlanFolder ?? "";
        values["Args"] = planFolder;

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

        var profileOverride = ExtractExecutionProfile(job, planYaml);
        AddRepoConfigsIfNeeded(job, planYaml, values);
        AddCreatePrOptions(job, values);

        return (values, planYaml, profileOverride);
    }


    private static string? ExtractExecutionProfile(JobItem job, PlanYaml planYaml)
    {
        if (job.TypedArgs is ExecutePlanArgs && !string.IsNullOrEmpty(planYaml.ExecutionProfile))
            return planYaml.ExecutionProfile;
        return null;
    }

    private void AddRepoConfigsIfNeeded(JobItem job, PlanYaml planYaml, Dictionary<string, string> values)
    {
        if (job.TypedArgs is not (ExecutePlanArgs or CreatePrArgs))
            return;

        var repoConfigs = BuildRepoConfigsYaml(planYaml, job.Project);
        if (!string.IsNullOrEmpty(repoConfigs))
            values["RepoConfigs"] = repoConfigs;
    }

    private static void AddCreatePrOptions(JobItem job, Dictionary<string, string> values)
    {
        if (job.TypedArgs is not CreatePrArgs pr)
            return;

        values["PrMerge"] = pr.Merge.ToString().ToLowerInvariant();
        values["PrDeleteBranch"] = pr.DeleteBranch.ToString().ToLowerInvariant();
        values["PrIncludeArtifacts"] = pr.IncludeArtifacts.ToString().ToLowerInvariant();
        values["PrDraft"] = pr.Draft.ToString().ToLowerInvariant();
        if (!string.IsNullOrEmpty(pr.Assignee))
            values["PrAssignee"] = pr.Assignee;
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

    private void SetTendrilEnvironment(ProcessStartInfo psi, JobItem job)
    {
        psi.Environment["TENDRIL_JOB_ID"] = job.Id;
        psi.Environment["TENDRIL_SESSION_ID"] = job.SessionId;
        var tendrilHome = _configService!.TendrilHome;
        if (!string.IsNullOrEmpty(tendrilHome))
            psi.Environment["TENDRIL_HOME"] = tendrilHome;
        psi.Environment["TENDRIL_CONFIG"] = _configService.ConfigPath;
        psi.Environment["TENDRIL_PLANS"] = _configService.PlanFolder;

        var statusFile = JobStatusFile.GetStatusFilePath(job.Id);
        psi.Environment["TENDRIL_CLI_LOG"] = statusFile;
        job.StatusFilePath = statusFile;

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
                repoRef?.BaseBranch ?? "main",
                repoRef?.SyncStrategy ?? "fetch",
                repoRef?.PrRule ?? "default",
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
                FindBaseBranchAcrossProjects(repoName),
                "fetch",
                "default",
                ReadOnly: true);
            AddRepoToConfigLines(lines, entry);
        }
    }

    private static void AddRepoToConfigLines(List<string> lines, RepoConfigEntry entry)
    {
        lines.Add($"- path: {entry.Path}");
        lines.Add($"  baseBranch: {entry.BaseBranch}");
        lines.Add($"  syncStrategy: {entry.SyncStrategy}");
        lines.Add($"  prRule: {entry.PrRule}");
        if (entry.ReadOnly)
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
