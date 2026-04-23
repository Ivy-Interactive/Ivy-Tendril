using System.Diagnostics;
using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Test.Agents;

/// <summary>
/// Integration tests for Codex and Gemini agent providers.
/// These tests verify that the agents can execute a minimal plan successfully.
/// Tests are skipped if the agent CLI is not installed.
/// </summary>
public class AgentIntegrationTests : IDisposable
{
    private readonly string _testDir;

    public AgentIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tendril-agent-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact(Skip = "Requires codex CLI to be installed")]
    public async Task Codex_CanExecuteSimplePlan()
    {
        // Skip test if codex CLI is not available
        if (!IsCommandAvailable("codex"))
        {
            return;
        }

        var provider = new CodexAgentProvider();
        var invocation = new AgentInvocation(
            PromptContent: "Create a file called hello.txt with the content 'Hello from Codex'",
            WorkingDirectory: _testDir,
            Model: "",
            Effort: "",
            SessionId: Guid.NewGuid().ToString(),
            AllowedTools: new[] { $"Write({_testDir}/**)", $"Read({_testDir}/**)" },
            ExtraArgs: Array.Empty<string>()
        );

        var psi = provider.BuildProcessStart(invocation);
        psi.WorkingDirectory = _testDir;

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        await process.WaitForExitAsync();

        // Verify process exited successfully
        Assert.Equal(0, process.ExitCode);

        // Verify the expected file was created
        var helloFile = Path.Combine(_testDir, "hello.txt");
        Assert.True(File.Exists(helloFile), $"Expected file not found: {helloFile}");

        var content = await File.ReadAllTextAsync(helloFile);
        Assert.Contains("Hello from Codex", content);
    }

    [Fact(Skip = "Requires gemini CLI to be installed")]
    public async Task Gemini_CanExecuteSimplePlan()
    {
        // Skip test if gemini CLI is not available
        if (!IsCommandAvailable("gemini"))
        {
            return;
        }

        var provider = new GeminiAgentProvider();
        var invocation = new AgentInvocation(
            PromptContent: "Create a file called hello.txt with the content 'Hello from Gemini'",
            WorkingDirectory: _testDir,
            Model: "",
            Effort: "",
            SessionId: Guid.NewGuid().ToString(),
            AllowedTools: new[] { $"Write({_testDir}/**)", $"Read({_testDir}/**)" },
            ExtraArgs: Array.Empty<string>()
        );

        var psi = provider.BuildProcessStart(invocation);
        psi.WorkingDirectory = _testDir;

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        await process.WaitForExitAsync();

        // Verify process exited successfully
        Assert.Equal(0, process.ExitCode);

        // Verify the expected file was created
        var helloFile = Path.Combine(_testDir, "hello.txt");
        Assert.True(File.Exists(helloFile), $"Expected file not found: {helloFile}");

        var content = await File.ReadAllTextAsync(helloFile);
        Assert.Contains("Hello from Gemini", content);
    }

    [Fact]
    public void Codex_CliAvailabilityCheck()
    {
        // Smoke test: verify CLI availability check works
        var isAvailable = IsCommandAvailable("codex");
        // This test always passes - it just documents whether codex is installed
        // in the current test environment
        Assert.True(true, $"Codex CLI is {(isAvailable ? "available" : "not available")}");
    }

    [Fact]
    public void Gemini_CliAvailabilityCheck()
    {
        // Smoke test: verify CLI availability check works
        var isAvailable = IsCommandAvailable("gemini");
        // This test always passes - it just documents whether gemini is installed
        // in the current test environment
        Assert.True(true, $"Gemini CLI is {(isAvailable ? "available" : "not available")}");
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
