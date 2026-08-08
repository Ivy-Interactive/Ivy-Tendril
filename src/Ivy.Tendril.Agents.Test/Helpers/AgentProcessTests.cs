using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Test.Helpers;

public class AgentProcessTests
{
    private static (string FileName, string[] Arguments) EchoSpec(string text) =>
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? ("cmd.exe", ["/c", "echo", text])
            : ("echo", [text]);

    [Fact]
    public async Task Start_SimpleCommand_Succeeds()
    {
        var (file, args) = EchoSpec("hello");
        var spec = new AgentProcessSpec
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = Path.GetTempPath(),
            Environment = new Dictionary<string, string>(),
            RedirectStdin = false,
        };

        using var process = AgentProcess.Start(spec);
        Assert.True(process.ProcessId > 0);
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public async Task ReadStdoutLinesAsync_ReadsOutput()
    {
        var (file, args) = EchoSpec("test output");
        var spec = new AgentProcessSpec
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = Path.GetTempPath(),
            Environment = new Dictionary<string, string>(),
            RedirectStdin = false,
        };

        using var process = AgentProcess.Start(spec);
        var lines = new List<string>();
        await foreach (var line in process.ReadStdoutLinesAsync())
            lines.Add(line);

        Assert.Contains(lines, l => l.Contains("test output"));
    }

    [Fact]
    public async Task WriteStdinAsync_AndClose_Works()
    {
        // Use a command that reads from stdin
        var spec = new AgentProcessSpec
        {
            FileName = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows) ? "findstr" : "cat",
            Arguments = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows) ? [".*"] : [],
            WorkingDirectory = Path.GetTempPath(),
            Environment = new Dictionary<string, string>(),
            RedirectStdin = true,
        };

        using var process = AgentProcess.Start(spec);
        await process.WriteStdinAsync("hello from stdin\n");
        process.CloseStdin();

        var lines = new List<string>();
        await foreach (var line in process.ReadStdoutLinesAsync())
            lines.Add(line);

        Assert.Contains(lines, l => l.Contains("hello from stdin"));
    }

    [Fact]
    public async Task WaitForExitAsync_CompletesAfterExit()
    {
        var (file, args) = EchoSpec("done");
        var spec = new AgentProcessSpec
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = Path.GetTempPath(),
            Environment = new Dictionary<string, string>(),
            RedirectStdin = false,
        };

        using var process = AgentProcess.Start(spec);
        await process.WaitForExitAsync();

        Assert.True(process.HasExited);
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void Kill_TerminatesProcess()
    {
        // Start a long-running process
        var spec = new AgentProcessSpec
        {
            FileName = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows) ? "timeout" : "sleep",
            Arguments = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows) ? ["/t", "60"] : ["60"],
            WorkingDirectory = Path.GetTempPath(),
            Environment = new Dictionary<string, string>(),
            RedirectStdin = true,
        };

        using var process = AgentProcess.Start(spec);
        Assert.False(process.HasExited);

        process.Kill();

        // Give it a moment to terminate
        Thread.Sleep(100);
        Assert.True(process.HasExited);
    }

    [Fact]
    public void Interrupt_TerminatesProcess()
    {
        var spec = new AgentProcessSpec
        {
            FileName = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows) ? "timeout" : "sleep",
            Arguments = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows) ? ["/t", "60"] : ["60"],
            WorkingDirectory = Path.GetTempPath(),
            Environment = new Dictionary<string, string>(),
            RedirectStdin = true,
        };

        using var process = AgentProcess.Start(spec);
        process.Interrupt();

        Thread.Sleep(200);
        Assert.True(process.HasExited);
    }
}
