using System.Diagnostics;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Promptware;

public record PromptwareRunOptions
{
    public required string Promptware { get; init; }
    public Dictionary<string, string> Values { get; init; } = new();
    public string? Profile { get; init; }
    public string? WorkingDir { get; init; }
    public string? PromptwarePath { get; init; }
}

public record PromptwareRunHandle : IDisposable
{
    internal Process? Process { get; init; }
    internal CancellationTokenSource Cts { get; init; } = new();
    internal string? LogFilePath { get; init; }
    internal string? CompiledPrompt { get; set; }
    internal string? CliCommand { get; set; }
    internal string Provider { get; init; } = "claude";
    internal IEventParser? EventParser { get; init; }

    /// <summary>Carries the job's identity (Id/Type/PlanFile) through to the completion log write.</summary>
    internal JobItem? LogJob { get; init; }
    internal DateTime StartedAt { get; init; }

    public bool IsRunning => Process is { HasExited: false };
    public int? ExitCode => Process is { HasExited: true } ? Process.ExitCode : null;
    public Task Completion { get; internal init; } = Task.CompletedTask;

    public void Cancel()
    {
        Cts.Cancel();
        if (Process is { HasExited: false })
        {
            try { Process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }

    public void Dispose()
    {
        Cancel();
        Process?.Dispose();
        Cts.Dispose();
    }
}

public interface IPromptwareRunner
{
    PromptwareRunHandle Run(PromptwareRunOptions options, IWriteStream<string> outputStream);
}

public class PromptwareRunner : IPromptwareRunner
{
    private readonly IConfigService _configService;
    private readonly IAgentRunner _agentRunner;
    private readonly ILogger<PromptwareRunner> _logger;

    public PromptwareRunner(IConfigService configService, IAgentRunner agentRunner, ILogger<PromptwareRunner> logger)
    {
        _configService = configService;
        _agentRunner = agentRunner;
        _logger = logger;
    }

    public PromptwareRunHandle Run(PromptwareRunOptions options, IWriteStream<string> outputStream)
    {
        var settings = _configService.Settings;
        var programFolder = PromptwareHelper.ResolvePromptwareFolder(options.Promptware, _configService.TendrilHome, options.PromptwarePath);
        var programMd = Path.Combine(programFolder, "Program.md");

        var workspaceDir = _configService.Projects.FirstOrDefault()?.RepoPaths.FirstOrDefault();
        var workingDir = string.IsNullOrEmpty(workspaceDir) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(workspaceDir) ?? Directory.GetCurrentDirectory();
        var vaultPath = PromptwareHelper.ResolveBrainwaresVaultDir(workingDir);

        if (vaultPath == null && !File.Exists(programMd))
            throw new FileNotFoundException($"Program.md not found at {programMd}", programMd);

        var values = new Dictionary<string, string>(options.Values);

        var jobContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PROMPTWARE_DIR"] = programFolder,
            ["TENDRIL_HOME"] = _configService.TendrilHome
        };

        var resolution = AgentProviderFactory.Resolve(_agentRunner, settings, options.Promptware, options.Profile, jobContext);
        var workDir = options.WorkingDir ?? programFolder;

        var jobId = JobIdAllocator.AllocateJobId(_configService.TendrilHome);
        // Agents pass this to `tendril job add-log` to target their own job log.
        values["TendrilJobId"] = jobId;
        var logJob = JobLogWriter.BuildCliRunJob(jobId, options.Promptware, values);
        var logFile = JobLogWriter.SeedLog(_configService.TendrilHome, logJob);
        var firmwareContext = new FirmwareContext(programFolder, values);
        var prompt = FirmwareCompiler.Compile(firmwareContext);

        logJob.CompiledPrompt = prompt;
        var promptPath = JobLogWriter.WritePrompt(_configService.TendrilHome, logJob);
        var promptFilePath = resolution.UsesStdinPrompt ? null : promptPath;

        var launchConfig = new AgentLaunchConfig
        {
            Prompt = prompt,
            WorkingDirectory = workDir,
            Model = string.IsNullOrEmpty(resolution.Model) ? null : resolution.Model,
            Effort = AgentProviderFactory.ParseEffort(resolution.Effort),
            PermissionMode = PermissionMode.FullAuto,
            AllowedTools = resolution.AllowedTools,
            WritableDirectories = ResolveWritableDirectories(options.Promptware, programFolder),
            ExtraArguments = resolution.ExtraArgs,
            PromptFilePath = promptFilePath,
        };

        var spec = resolution.Cli.BuildProcessSpec(launchConfig);
        var psi = AgentProcessHelper.ToPsi(spec);

        var tendrilHome = _configService.TendrilHome;
        if (!string.IsNullOrEmpty(tendrilHome))
            psi.Environment["TENDRIL_HOME"] = tendrilHome;
        psi.Environment["TENDRIL_CONFIG"] = _configService.ConfigPath;
        psi.Environment["TENDRIL_PLANS"] = _configService.PlanFolder;

        AgentProcessHelper.EnsureTendrilOnPath(psi);
        AgentProcessHelper.ResolveCommandShim(psi);

        _logger.LogInformation("PromptwareRunner: launching {Promptware} via {Provider} (model={Model}, effort={Effort})",
            options.Promptware, resolution.AgentId, resolution.Model, resolution.Effort);

        var cliCommand = AgentProcessHelper.FormatCliCommand(psi);
        var startedAt = DateTime.UtcNow;

        // Open the raw and eventwire appenders before the process starts, so a killed run still leaves a
        // complete record. Assigning LogFilePath is what opens them.
        logJob.Provider = resolution.AgentId;
        logJob.EventParser = _agentRunner.GetParser(resolution.AgentId);
        logJob.StartedAt = startedAt;
        logJob.CliCommand = cliCommand;
        logJob.LogFilePath = logFile;

        var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start agent process");

        if (resolution.UsesStdinPrompt && psi.RedirectStandardInput)
        {
            process.StandardInput.Write(prompt);
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }

        var cts = new CancellationTokenSource();
        var handle = new PromptwareRunHandle
        {
            Process = process,
            Cts = cts,
            LogFilePath = logFile,
            CompiledPrompt = prompt,
            CliCommand = cliCommand,
            Provider = resolution.AgentId,
            EventParser = logJob.EventParser,
            LogJob = logJob,
            StartedAt = startedAt
        };

        handle = handle with
        {
            Completion = Task.Run(() => PipeOutputAndLog(process, outputStream, handle, cts.Token), cts.Token)
        };

        return handle;
    }

    private IReadOnlyList<string> ResolveWritableDirectories(string promptwareType, string promptwareFolder)
    {
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

        var workspaceDir = _configService.Projects.FirstOrDefault()?.RepoPaths.FirstOrDefault();
        var workingDir = string.IsNullOrEmpty(workspaceDir) ? Directory.GetCurrentDirectory() : workspaceDir;
        var vaultPath = PromptwareHelper.ResolveBrainwaresVaultDir(workingDir);
        if (vaultPath != null)
        {
            var memoriesDir = Path.Combine(vaultPath, "memories");
            if (!memoriesDir.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase))
                dirs.Add(memoriesDir);
            if (!vaultPath.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase))
                dirs.Add(vaultPath);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalMemoriesDir = Path.Combine(userProfile, ".config", "brainwares", "memories");
        if (!globalMemoriesDir.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase))
            dirs.Add(globalMemoriesDir);
        var globalConfigDir = Path.Combine(userProfile, ".config", "brainwares");
        if (!globalConfigDir.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase))
            dirs.Add(globalConfigDir);

        return [.. dirs];
    }

    private static async Task PipeOutputAndLog(Process process, IWriteStream<string> stream, PromptwareRunHandle handle, CancellationToken ct)
    {
        // The job owns the parser and the raw/eventwire appenders (opened when Run assigned LogFilePath),
        // so output is recorded as it arrives rather than buffered until completion — a killed run keeps
        // everything it produced. The UI stream is fed from the job's event observable so the line is
        // parsed exactly once.
        var job = handle.LogJob!;
        using var subscription = job.OutputObservable.Subscribe(stream.Write);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) job.EnqueueOutput(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) job.EnqueueOutput($"[stderr] {e.Data}");
        };
        process.EnableRaisingEvents = true;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
            await Task.Delay(100, ct);
        }
        catch (OperationCanceledException) { }

        job.FlushParser();

        if (!string.IsNullOrEmpty(handle.LogFilePath))
        {
            try
            {
                var completedAt = DateTime.UtcNow;
                job.Status = process.HasExited && process.ExitCode == 0 ? JobStatus.Completed : JobStatus.Failed;
                job.CompletedAt = completedAt;
                job.DurationSeconds = (int)(completedAt - handle.StartedAt).TotalSeconds;
                job.ExitCode = process.HasExited ? process.ExitCode : null;

                JobLogWriter.WriteLog(job);
            }
            catch { }
            finally
            {
                job.CloseLogWriters();
            }

            handle.CompiledPrompt = null;
            handle.CliCommand = null;
        }
    }

}
