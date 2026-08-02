using System.Diagnostics;
using System.Text;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Xunit;

namespace Ivy.Tendril.Test.Helpers;

public class AgentProcessHelperTests
{
    [Fact]
    public void ToPsi_SetsUtf8EncodingOnAllRedirectedStreams()
    {
        var spec = new AgentProcessSpec
        {
            FileName = "test",
            Arguments = [],
            WorkingDirectory = ".",
            Environment = new Dictionary<string, string>(),
            RedirectStdin = true,
            RedirectStdout = true,
            RedirectStderr = true
        };

        var psi = AgentProcessHelper.ToPsi(spec);

        Assert.NotNull(psi.StandardInputEncoding);
        Assert.Equal(65001, psi.StandardInputEncoding.CodePage);
        Assert.NotNull(psi.StandardOutputEncoding);
        Assert.Equal(65001, psi.StandardOutputEncoding.CodePage);
        Assert.NotNull(psi.StandardErrorEncoding);
        Assert.Equal(65001, psi.StandardErrorEncoding.CodePage);
    }

    [Fact]
    public void ToPsi_UsesBomlessUtf8()
    {
        var spec = new AgentProcessSpec
        {
            FileName = "test",
            Arguments = [],
            WorkingDirectory = ".",
            Environment = new Dictionary<string, string>(),
            RedirectStdin = true,
            RedirectStdout = true,
            RedirectStderr = true
        };

        var psi = AgentProcessHelper.ToPsi(spec);

        Assert.NotNull(psi.StandardInputEncoding);
        Assert.Empty(psi.StandardInputEncoding.GetPreamble());
        Assert.NotNull(psi.StandardOutputEncoding);
        Assert.Empty(psi.StandardOutputEncoding.GetPreamble());
        Assert.NotNull(psi.StandardErrorEncoding);
        Assert.Empty(psi.StandardErrorEncoding.GetPreamble());
    }

    [Fact]
    public void ToPsi_LeavesEncodingNullForNonRedirectedStreams()
    {
        var spec = new AgentProcessSpec
        {
            FileName = "test",
            Arguments = [],
            WorkingDirectory = ".",
            Environment = new Dictionary<string, string>(),
            RedirectStdin = false,
            RedirectStdout = false,
            RedirectStderr = false
        };

        var psi = AgentProcessHelper.ToPsi(spec);

        Assert.Null(psi.StandardInputEncoding);
        Assert.Null(psi.StandardOutputEncoding);
        Assert.Null(psi.StandardErrorEncoding);
    }

    [Fact]
    public void ToPsi_WithAllEncodings_ProcessStartsSuccessfully()
    {
        var spec = new AgentProcessSpec
        {
            FileName = OperatingSystem.IsWindows() ? "cmd" : "echo",
            Arguments = OperatingSystem.IsWindows() ? ["/c", "echo", "test"] : ["test"],
            WorkingDirectory = ".",
            Environment = new Dictionary<string, string>(),
            RedirectStdin = true,
            RedirectStdout = true,
            RedirectStderr = true,
            CreateNoWindow = true
        };

        var psi = AgentProcessHelper.ToPsi(spec);
        using var process = Process.Start(psi);

        Assert.NotNull(process);
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void ToPsi_RoundTripsNonAsciiThroughStdoutAndStdin()
    {
        var testString = "em dash — arrow → CJK 日本";

        var spec = new AgentProcessSpec
        {
            FileName = OperatingSystem.IsWindows() ? "cmd" : "cat",
            Arguments = OperatingSystem.IsWindows() ? ["/c", "findstr", ".*"] : [],
            WorkingDirectory = ".",
            Environment = new Dictionary<string, string>(),
            RedirectStdin = true,
            RedirectStdout = true,
            RedirectStderr = true,
            CreateNoWindow = true
        };

        var psi = AgentProcessHelper.ToPsi(spec);
        using var process = Process.Start(psi);
        Assert.NotNull(process);

        process.StandardInput.WriteLine(testString);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(5000));

        Assert.Equal(testString, output.Trim());
    }
}
