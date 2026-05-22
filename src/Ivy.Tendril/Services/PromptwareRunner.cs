using System.Diagnostics;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Agents;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

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

        if (!File.Exists(programMd))
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
        var logFile = FirmwareCompiler.GetLogFile(programFolder, jobId);
        var firmwareContext = new FirmwareContext(programFolder, values);
        var prompt = FirmwareCompiler.Compile(firmwareContext);

        string? promptFilePath = null;
        if (!resolution.UsesStdinPrompt)
        {
            var tempDir = Path.GetTempPath();
            promptFilePath = Path.Combine(tempDir, $"prompt-{Guid.NewGuid():N}.md");
            File.WriteAllText(promptFilePath, prompt);
        }

        var launchConfig = new AgentLaunchConfig
        {
            Prompt = prompt,
            WorkingDirectory = workDir,
            Model = string.IsNullOrEmpty(resolution.Model) ? null : resolution.Model,
            Effort = AgentProviderFactory.ParseEffort(resolution.Effort),
            PermissionMode = PermissionMode.FullAuto,
            AllowedTools = resolution.AllowedTools,
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

        _logger.LogInformation("PromptwareRunner: launching {Promptware} via {Provider} (model={Model}, effort={Effort})",
            options.Promptware, resolution.AgentId, resolution.Model, resolution.Effort);

        var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start agent process");

        if (resolution.UsesStdinPrompt && psi.RedirectStandardInput)
        {
            process.StandardInput.Write(prompt);
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }

        var cliCommand = AgentProcessHelper.FormatCliCommand(psi);

        var cts = new CancellationTokenSource();
        var handle = new PromptwareRunHandle
        {
            Process = process,
            Cts = cts,
            LogFilePath = logFile,
            CompiledPrompt = prompt,
            CliCommand = cliCommand,
            Provider = resolution.AgentId,
            EventParser = _agentRunner.GetParser(resolution.AgentId)
        };

        handle = handle with
        {
            Completion = Task.Run(() => PipeOutputAndLog(process, outputStream, handle, cts.Token), cts.Token)
        };

        return handle;
    }

    private static async Task PipeOutputAndLog(Process process, IWriteStream<string> stream, PromptwareRunHandle handle, CancellationToken ct)
    {
        var outputLines = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stream.Write(e.Data);
                outputLines.Add(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stream.Write($"[stderr] {e.Data}");
                outputLines.Add($"[stderr] {e.Data}");
            }
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

        if (!string.IsNullOrEmpty(handle.LogFilePath))
        {
            try
            {
                var job = new JobItem
                {
                    Provider = handle.Provider,
                    EventParser = handle.EventParser,
                    Status = process.HasExited && process.ExitCode == 0 ? JobStatus.Completed : JobStatus.Failed,
                    StartedAt = null,
                    CompletedAt = DateTime.UtcNow,
                    ExitCode = process.HasExited ? process.ExitCode : null,
                    LogFilePath = handle.LogFilePath,
                    CompiledPrompt = handle.CompiledPrompt,
                    CliCommand = handle.CliCommand
                };
                foreach (var line in outputLines)
                    job.EnqueueOutput(line);

                PromptwareLogWriter.WriteLog(job);
                PromptwareLogWriter.WriteRawLog(handle.LogFilePath, outputLines);
            }
            catch { }

            handle.CompiledPrompt = null;
            handle.CliCommand = null;
        }
    }

}
