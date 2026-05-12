using System.Collections.Concurrent;
using System.Diagnostics;
using Ivy.Tendril.Test.End2End.Configuration;

namespace Ivy.Tendril.Test.End2End.Helpers;

public record PromptwareResult(
    int ExitCode,
    IReadOnlyList<string> StdoutLines,
    IReadOnlyList<string> StderrLines,
    TimeSpan Duration);

public class PromptwareRunner
{
    private readonly string _tendrilProjectPath;
    private readonly string _tendrilHome;

    public PromptwareRunner(string tendrilProjectPath, string tendrilHome)
    {
        _tendrilProjectPath = tendrilProjectPath;
        _tendrilHome = tendrilHome;
    }

    public async Task<PromptwareResult> RunAsync(
        string promptwareName,
        string[] args,
        string workingDir,
        Dictionary<string, string>? extraValues = null,
        string? profile = null,
        string? agent = null,
        string? cliLogPath = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        timeout ??= TimeSpan.FromSeconds(TestSettingsProvider.Get().PlanExecutionTimeoutSeconds);

        var arguments = new List<string>
        {
            "run", "--project", _tendrilProjectPath, "--"
        };

        arguments.Add("promptware");
        arguments.Add(promptwareName);
        arguments.AddRange(args);

        if (!string.IsNullOrEmpty(workingDir))
        {
            arguments.Add("--working-dir");
            arguments.Add(workingDir);
        }

        if (!string.IsNullOrEmpty(profile))
        {
            arguments.Add("--profile");
            arguments.Add(profile);
        }

        if (!string.IsNullOrEmpty(agent))
        {
            arguments.Add("--agent");
            arguments.Add(agent);
        }

        if (!string.IsNullOrEmpty(cliLogPath))
        {
            arguments.Add("--cli-log");
            arguments.Add(cliLogPath);
        }

        if (extraValues != null)
        {
            foreach (var (key, value) in extraValues)
            {
                arguments.Add("--value");
                arguments.Add($"{key}={value}");
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir
        };

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        psi.Environment["TENDRIL_HOME"] = _tendrilHome;
        psi.Environment["TENDRIL_CONFIG"] = Path.Combine(_tendrilHome, "config.yaml");
        psi.Environment["TENDRIL_PLANS"] = Path.Combine(_tendrilHome, "Plans");
        psi.Environment["TENDRIL_E2E"] = "1";

        var stdout = new ConcurrentBag<string>();
        var stderr = new ConcurrentBag<string>();
        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();

        var sw = Stopwatch.StartNew();

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Failed to start: dotnet {string.Join(" ", arguments)}");

        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
                stdoutLines.Add(line);
        }, ct);

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(ct) is { } line)
                stderrLines.Add(line);
        }, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout.Value);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            sw.Stop();
            await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(5000, CancellationToken.None));
            throw new TimeoutException(
                $"Promptware '{promptwareName}' timed out after {timeout.Value.TotalSeconds}s.\n" +
                $"Last stdout: {string.Join("\n", stdoutLines.TakeLast(20))}\n" +
                $"Last stderr: {string.Join("\n", stderrLines.TakeLast(20))}");
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        sw.Stop();

        return new PromptwareResult(
            process.ExitCode,
            stdoutLines,
            stderrLines,
            sw.Elapsed);
    }
}
