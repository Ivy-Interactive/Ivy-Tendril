using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;

namespace Ivy.Tendril.Agents.Test.Antigravity;

public class AntigravityCliTests
{
    private readonly AntigravityCli _cli = new();

    [Fact]
    public void Id_IsAntigravity()
    {
        Assert.Equal("antigravity", _cli.Id);
    }

    [Fact]
    public void DisplayName_IsAntigravity()
    {
        Assert.Equal("Antigravity", _cli.DisplayName);
    }

    [Fact]
    public void Capabilities_Correct()
    {
        var expected = AgentCapabilities.StdinPrompt |
                       AgentCapabilities.StreamJsonOutput |
                       AgentCapabilities.ModelSelection |
                       AgentCapabilities.EffortControl |
                       AgentCapabilities.DirectoryRestriction |
                       AgentCapabilities.HealthCheck |
                       AgentCapabilities.ExtraArgPassthrough;

        Assert.Equal(expected, _cli.Capabilities);
    }

    [Fact]
    public void SupportedTransports_IsCliSpawn()
    {
        Assert.Equal(TransportKind.CliSpawn, _cli.SupportedTransports);
    }

    [Fact]
    public void PromptTransport_IsStdin()
    {
        Assert.Equal(PromptTransport.Stdin, _cli.PromptTransport);
    }

    [Fact]
    public void PreferredOutputFormat_IsStreamJson()
    {
        Assert.Equal(OutputFormat.StreamJson, _cli.PreferredOutputFormat);
    }

    [Fact]
    public void DefaultProfiles_Correct()
    {
        Assert.Equal(3, _cli.DefaultProfiles.Count);
        Assert.All(_cli.DefaultProfiles, p => Assert.Equal("gemini-3.7-flash", p.Model));
    }

    [Fact]
    public void BuildProcessSpec_BasicInvocation_HasCorrectFileNameAndArgs()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("agy", spec.FileName);
        Assert.Equal("/tmp", spec.WorkingDirectory);
        Assert.Contains("--print", spec.Arguments);
        Assert.Contains("--dangerously-skip-permissions", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_WithTimeout_IncludesConfiguredPrintTimeout()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            Timeout = TimeSpan.FromMinutes(30),
        };

        var spec = _cli.BuildProcessSpec(config);
        var args = spec.Arguments.ToList();

        var timeoutIdx = args.IndexOf("--print-timeout");
        Assert.True(timeoutIdx >= 0);
        Assert.Equal("1800s", args[timeoutIdx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_DoesNotPassConversationForNewSession()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            SessionId = "sess-123"
        };

        var spec = _cli.BuildProcessSpec(config);
        var args = spec.Arguments.ToList();

        Assert.DoesNotContain("--conversation", args);
        Assert.DoesNotContain("sess-123", args);
    }

    [Fact]
    public void BuildProcessSpec_IncludesWritableDirectories()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            WritableDirectories = ["/dir1", "/dir2"]
        };

        var spec = _cli.BuildProcessSpec(config);
        var args = spec.Arguments.ToList();

        var idx1 = args.IndexOf("/dir1");
        Assert.True(idx1 > 0);
        Assert.Equal("--add-dir", args[idx1 - 1]);

        var idx2 = args.IndexOf("/dir2");
        Assert.True(idx2 > 0);
        Assert.Equal("--add-dir", args[idx2 - 1]);
    }

    [Fact]
    public void BuildProcessSpec_IncludesExtraArguments()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            ExtraArguments = ["--custom-flag", "value"]
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Contains("--custom-flag", spec.Arguments);
        Assert.Contains("value", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_IncludesEnvironmentVariables()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["CUSTOM_ENV"] = "custom-val"
            }
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("true", spec.Environment["CI"]);
        Assert.Equal("dumb", spec.Environment["TERM"]);
        Assert.Equal("custom-val", spec.Environment["CUSTOM_ENV"]);
    }
}
