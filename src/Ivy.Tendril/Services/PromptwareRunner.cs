using System.Diagnostics;
using Ivy.Tendril.Helpers;
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
    private readonly ILogger<PromptwareRunner> _logger;

    public PromptwareRunner(IConfigService configService, ILogger<PromptwareRunner> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public PromptwareRunHandle Run(PromptwareRunOptions options, IWriteStream<string> outputStream)
    {
        var settings = _configService.Settings;
        var programFolder = ResolvePromptwareFolder(options.Promptware, _configService.TendrilHome, options.PromptwarePath);
        var programMd = Path.Combine(programFolder, "Program.md");

        if (!File.Exists(programMd))
            throw new FileNotFoundException($"Program.md not found at {programMd}", programMd);

        var values = new Dictionary<string, string>(options.Values);
        var resolution = AgentProviderFactory.Resolve(settings, options.Promptware, options.Profile);
        var workDir = options.WorkingDir ?? programFolder;

        var logFile = FirmwareCompiler.GetNextLogFile(programFolder, values);
        var firmwareContext = new FirmwareContext(programFolder, logFile, values);
        var prompt = FirmwareCompiler.Compile(firmwareContext);

        var invocation = new AgentInvocation(
            PromptContent: prompt,
            WorkingDirectory: workDir,
            Model: resolution.Model,
            Effort: resolution.Effort,
            SessionId: "",
            AllowedTools: resolution.AllowedTools,
            ExtraArgs: resolution.ExtraArgs);

        var psi = resolution.Provider.BuildProcessStart(invocation);

        var tendrilHome = _configService.TendrilHome;
        if (!string.IsNullOrEmpty(tendrilHome))
            psi.Environment["TENDRIL_HOME"] = tendrilHome;
        psi.Environment["TENDRIL_CONFIG"] = _configService.ConfigPath;
        psi.Environment["TENDRIL_PLANS"] = _configService.PlanFolder;

        JobLauncher.EnsureTendrilOnPath(psi);

        _logger.LogInformation("PromptwareRunner: launching {Promptware} via {Provider} (model={Model}, effort={Effort})",
            options.Promptware, resolution.Provider.Name, resolution.Model, resolution.Effort);

        var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start agent process");

        if (resolution.Provider.UsesStdinPrompt && psi.RedirectStandardInput)
        {
            process.StandardInput.Write(prompt);
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }

        var cts = new CancellationTokenSource();
        var completion = Task.Run(() => PipeOutput(process, outputStream, cts.Token), cts.Token);

        return new PromptwareRunHandle
        {
            Process = process,
            Cts = cts,
            Completion = completion
        };
    }

    private static async Task PipeOutput(Process process, IWriteStream<string> stream, CancellationToken ct)
    {
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                stream.Write(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                stream.Write($"[stderr] {e.Data}");
        };
        process.EnableRaisingEvents = true;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
            // Give output handlers a moment to flush
            await Task.Delay(100, ct);
        }
        catch (OperationCanceledException) { }
    }

    private static string ResolvePromptwareFolder(string promptwareName, string? tendrilHome, string? promptwarePath)
    {
        if (!string.IsNullOrEmpty(promptwarePath))
        {
            var overrideFolder = Path.Combine(promptwarePath, promptwareName);
            if (File.Exists(Path.Combine(overrideFolder, "Program.md")))
                return overrideFolder;
        }

        var sourceRoot = PromptwareHelper.ResolvePromptsRoot(tendrilHome);
        var sourceFolder = Path.Combine(sourceRoot, promptwareName);

        if (File.Exists(Path.Combine(sourceFolder, "Program.md")))
            return sourceFolder;

        tendrilHome ??= Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            var deployedRoot = Path.Combine(tendrilHome, "Promptwares");
            var deployedFolder = Path.Combine(deployedRoot, promptwareName);
            if (File.Exists(Path.Combine(deployedFolder, "Program.md")))
                return deployedFolder;
        }

        return sourceFolder;
    }
}
