using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Test.OpenCode;

public class OpenCodeCliTests
{
    private readonly OpenCodeCli _cli = new();

    [Fact]
    public void Id_IsOpenCode()
    {
        Assert.Equal("opencode", _cli.Id);
    }

    [Fact]
    public void DisplayName_IsOpenCode()
    {
        Assert.Equal("OpenCode", _cli.DisplayName);
    }

    [Fact]
    public void Capabilities_IncludesStdinPrompt()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.StdinPrompt));
    }

    [Fact]
    public void Capabilities_IncludesStreamJsonOutput()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.StreamJsonOutput));
    }

    [Fact]
    public void Capabilities_IncludesCostInOutput()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.CostInOutput));
    }

    [Fact]
    public void Capabilities_IncludesModelSelection()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.ModelSelection));
    }

    [Fact]
    public void Capabilities_IncludesEffortControl()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.EffortControl));
    }

    [Fact]
    public void Capabilities_IncludesSessionResume()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.SessionResume));
    }

    [Fact]
    public void Capabilities_IncludesHealthCheck()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.HealthCheck));
    }

    [Fact]
    public void Capabilities_IncludesExtraArgPassthrough()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.ExtraArgPassthrough));
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

    [Theory]
    [InlineData(CanonicalTools.Read, "read")]
    [InlineData(CanonicalTools.Write, "write")]
    [InlineData(CanonicalTools.Edit, "edit")]
    [InlineData(CanonicalTools.Bash, "bash")]
    [InlineData(CanonicalTools.Glob, "glob")]
    [InlineData(CanonicalTools.Grep, "search")]
    [InlineData(CanonicalTools.WebFetch, "web_fetch")]
    [InlineData(CanonicalTools.WebSearch, "web_search")]
    public void TranslateToolName_KnownTool_ReturnsNativeName(string canonical, string expected)
    {
        Assert.Equal(expected, _cli.TranslateToolName(canonical));
    }

    [Fact]
    public void TranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_cli.TranslateToolName("SomeUnknownTool"));
    }

    [Theory]
    [InlineData("read", CanonicalTools.Read)]
    [InlineData("write", CanonicalTools.Write)]
    [InlineData("edit", CanonicalTools.Edit)]
    [InlineData("bash", CanonicalTools.Bash)]
    [InlineData("glob", CanonicalTools.Glob)]
    [InlineData("search", CanonicalTools.Grep)]
    [InlineData("web_fetch", CanonicalTools.WebFetch)]
    [InlineData("web_search", CanonicalTools.WebSearch)]
    public void ReverseTranslateToolName_KnownTool_ReturnsCanonical(string native, string expected)
    {
        Assert.Equal(expected, _cli.ReverseTranslateToolName(native));
    }

    [Fact]
    public void ReverseTranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_cli.ReverseTranslateToolName("unknown_tool"));
    }

    [Fact]
    public void ExtractWritableDirectories_AlwaysReturnsEmpty()
    {
        var dirs = _cli.ExtractWritableDirectories(["read", "write", "bash"]);
        Assert.Empty(dirs);
    }

    [Fact]
    public void BuildProcessSpec_MinimalConfig_ProducesCorrectArgs()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("opencode", spec.FileName);
        Assert.Contains("run", spec.Arguments);
        Assert.Contains("--dangerously-skip-permissions", spec.Arguments);
        Assert.Contains("--format", spec.Arguments);
        Assert.Contains("json", spec.Arguments);
        Assert.Equal("Hello", spec.StdinContent);
        Assert.Equal("/tmp", spec.WorkingDirectory);
        Assert.True(spec.RedirectStdin);
    }

    [Fact]
    public void BuildProcessSpec_WithModel_IncludesModelFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            Model = "anthropic/claude-sonnet-4-6",
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--model");
        Assert.True(idx >= 0);
        Assert.Equal("anthropic/claude-sonnet-4-6", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithEffort_IncludesVariantFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            Effort = EffortLevel.High,
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--variant");
        Assert.True(idx >= 0);
        Assert.Equal("high", spec.Arguments[idx + 1]);
    }

    [Theory]
    [InlineData(EffortLevel.Low, "low")]
    [InlineData(EffortLevel.Medium, "medium")]
    [InlineData(EffortLevel.High, "high")]
    [InlineData(EffortLevel.XHigh, "max")]
    [InlineData(EffortLevel.Max, "max")]
    public void BuildProcessSpec_AllEffortLevels_MapCorrectly(EffortLevel level, string expected)
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            Effort = level,
        };

        var spec = _cli.BuildProcessSpec(config);
        var idx = spec.Arguments.ToList().IndexOf("--variant");
        Assert.Equal(expected, spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithSession_IncludesSessionFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            SessionId = "my-session-123",
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--session");
        Assert.True(idx >= 0);
        Assert.Equal("my-session-123", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithExtraArgs_AppendsToEnd()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            ExtraArguments = ["--custom", "value"],
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Contains("--custom", spec.Arguments);
        Assert.Contains("value", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_Environment_SetsCiAndTerm()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("true", spec.Environment["CI"]);
        Assert.Equal("dumb", spec.Environment["TERM"]);
    }

    [Fact]
    public void BuildProcessSpec_WithEnvironmentOverrides_MergesCorrectly()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["CUSTOM_VAR"] = "custom_value",
                ["CI"] = "false",
            },
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("false", spec.Environment["CI"]);
        Assert.Equal("custom_value", spec.Environment["CUSTOM_VAR"]);
    }

    [Fact]
    public void BuildProcessSpec_NoModel_DoesNotIncludeModelFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.DoesNotContain("--model", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_NoEffort_DoesNotIncludeVariantFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.DoesNotContain("--variant", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_NoSession_DoesNotIncludeSessionFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.DoesNotContain("--session", spec.Arguments);
    }

    [Fact]
    public void GetDefaultEnvironment_ContainsCiTrue()
    {
        var env = _cli.GetDefaultEnvironment();
        Assert.Equal("true", env["CI"]);
    }

    [Fact]
    public void GetDefaultEnvironment_ContainsTermDumb()
    {
        var env = _cli.GetDefaultEnvironment();
        Assert.Equal("dumb", env["TERM"]);
    }
}
