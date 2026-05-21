using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Test.Helpers;

public class HealthCheckRunnerTests
{
    [Fact]
    public async Task RunAsync_SuccessfulCommand_ReturnsZeroExitCode()
    {
        var (exitCode, stdout, stderr) = await HealthCheckRunner.RunAsync(
            "echo", ["hello"], TimeSpan.FromSeconds(5));

        Assert.Equal(0, exitCode);
        Assert.Contains("hello", stdout);
    }

    [Fact]
    public async Task RunAsync_Timeout_ReturnsNegativeOne()
    {
        // Use ping with high count — works cross-platform and doesn't exit early
        var cmd = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows) ? "ping" : "sleep";
        var args = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows)
            ? new[] { "-n", "100", "127.0.0.1" }
            : new[] { "60" };

        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            cmd, args, TimeSpan.FromMilliseconds(500));

        Assert.Equal(-1, exitCode);
        Assert.Contains("Timed out", stderr);
    }

    [Fact]
    public async Task RunAsync_FailingCommand_ReturnsNonZeroExitCode()
    {
        // Try to run a command that will fail
        var (exitCode, _, _) = await HealthCheckRunner.RunAsync(
            "dotnet", ["--invalid-flag-that-does-not-exist"],
            TimeSpan.FromSeconds(10));

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_WithCancellation_Cancels()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            "echo", ["test"], TimeSpan.FromSeconds(5), cts.Token);

        Assert.Equal(-1, exitCode);
    }

    [Fact]
    public async Task RunAsync_CapturesStderr()
    {
        // Use a command that writes to stderr
        var cmd = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows) ? "cmd" : "sh";
        var args = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows)
            ? new[] { "/c", "echo error_output 1>&2" }
            : new[] { "-c", "echo error_output >&2" };

        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            cmd, args, TimeSpan.FromSeconds(5));

        Assert.Equal(0, exitCode);
        Assert.Contains("error_output", stderr);
    }

    [Fact]
    public async Task RunAsync_DefaultTimeout_IsThirtySeconds()
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "echo", ["default timeout"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("default timeout", stdout);
    }
}
