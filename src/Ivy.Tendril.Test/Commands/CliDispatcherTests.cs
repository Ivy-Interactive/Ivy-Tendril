using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Ivy.Tendril.Commands;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Testing;

namespace Ivy.Tendril.Test.Commands;

public class CliDispatcherTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    [InlineData("help")]
    public void Classify_BareHelpToken_ReturnsHelp(string token)
    {
        Assert.Equal(CliInvocationKind.Help, CliDispatcher.Classify([token]));
    }

    [Theory]
    [InlineData("job", "--help")]
    [InlineData("project", "add", "--help")]
    [InlineData("add", "project", "--help")]
    public void Classify_HelpTokenAnywhereInArgs_ReturnsHelp(params string[] args)
    {
        Assert.Equal(CliInvocationKind.Help, CliDispatcher.Classify(args));
    }

    [Theory]
    [InlineData("agent-instructions")]
    [InlineData("job")]
    [InlineData("plan")]
    [InlineData("project")]
    [InlineData("config")]
    [InlineData("verification")]
    [InlineData("trash")]
    [InlineData("models")]
    [InlineData("doctor")]
    [InlineData("run")]
    [InlineData("version")]
    [InlineData("report-bug")]
    public void Classify_RegisteredTopLevelCommand_ReturnsCliCommand(string command)
    {
        Assert.Equal(CliInvocationKind.CliCommand, CliDispatcher.Classify([command]));
    }

    [Fact]
    public void Classify_VersionFlag_ReturnsVersion()
    {
        Assert.Equal(CliInvocationKind.Version, CliDispatcher.Classify(["--version"]));
    }

    [Theory]
    [InlineData("mcp")]
    [InlineData("hash-password")]
    public void Classify_LegacyCommand_ReturnsLegacyCliCommand(string command)
    {
        Assert.Equal(CliInvocationKind.LegacyCliCommand, CliDispatcher.Classify([command]));
    }

    [Fact]
    public void Classify_LegacyCommandWithArgs_ReturnsLegacyCliCommand()
    {
        Assert.Equal(CliInvocationKind.LegacyCliCommand, CliDispatcher.Classify(["hash-password", "pw"]));
    }

    [Fact]
    public void Classify_EmptyArgs_ReturnsServerLaunch()
    {
        Assert.Equal(CliInvocationKind.ServerLaunch, CliDispatcher.Classify([]));
    }

    [Fact]
    public void Classify_PortFlag_ReturnsServerLaunch()
    {
        Assert.Equal(CliInvocationKind.ServerLaunch, CliDispatcher.Classify(["--port", "5011"]));
    }

    [Fact]
    public void Classify_FindAvailablePortFlag_ReturnsServerLaunch()
    {
        Assert.Equal(CliInvocationKind.ServerLaunch, CliDispatcher.Classify(["--find-available-port"]));
    }

    [Theory]
    [InlineData("add", "project")]
    [InlineData("--hepl")]
    [InlineData("nonsense")]
    public void Classify_UnrecognizedArgs_ReturnsUnknown(params string[] args)
    {
        var kind = CliDispatcher.Classify(args);
        Assert.Equal(CliInvocationKind.Unknown, kind);
        Assert.NotEqual(CliInvocationKind.ServerLaunch, kind);
    }

    [Fact]
    public void TopLevelCommands_MatchesEveryCommandListedByHelp()
    {
        var console = new TestConsole();
        var app = Program.ConfigureCliCommands(new ServiceCollection(), console);
        app.Run(["--help"]);

        var output = console.Output;
        var commandsSectionStart = output.IndexOf("COMMANDS:", StringComparison.Ordinal);
        Assert.True(commandsSectionStart >= 0, "Expected a COMMANDS: section in --help output.");

        var commandsSection = output[commandsSectionStart..];
        var commandNames = Regex.Matches(commandsSection, @"^\s{4}(\S+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(commandNames);
        foreach (var name in commandNames)
        {
            Assert.Contains(name, CliDispatcher.TopLevelCommands);
        }
    }

    [Fact]
    public void IsPortInUse_ReturnsTrue_WhenOnlyIPv6LoopbackIsBound()
    {
        var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.True(Program.IsPortInUse(port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void IsPortInUse_ReturnsFalse_WhenPortIsFree()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Assert.False(Program.IsPortInUse(port));
    }
}
