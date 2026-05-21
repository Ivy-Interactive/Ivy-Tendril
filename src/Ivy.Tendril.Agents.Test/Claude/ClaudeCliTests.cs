using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;

namespace Ivy.Tendril.Agents.Test.Claude;

public class ClaudeCliTests
{
    private readonly ClaudeCli _cli = new();

    [Fact]
    public void Id_IsClaude()
    {
        Assert.Equal(AgentId.Claude, _cli.Id);
    }

    [Fact]
    public void DisplayName_IsClaudeCode()
    {
        Assert.Equal("Claude Code", _cli.DisplayName);
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
    public void Capabilities_IncludesModelSelection()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.ModelSelection));
    }

    [Fact]
    public void Capabilities_IncludesMaxTurns()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.MaxTurns));
    }

    [Fact]
    public void Capabilities_IncludesCostInOutput()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.CostInOutput));
    }

    [Fact]
    public void Capabilities_IncludesEffortControl()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.EffortControl));
    }

    [Fact]
    public void Capabilities_IncludesPermissionDenialReporting()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.PermissionDenialReporting));
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
    public void TranslateToolName_KnownTool_ReturnsNativeName()
    {
        Assert.Equal("Read", _cli.TranslateToolName(CanonicalTools.Read));
        Assert.Equal("Bash", _cli.TranslateToolName(CanonicalTools.Bash));
        Assert.Equal("WebFetch", _cli.TranslateToolName(CanonicalTools.WebFetch));
    }

    [Fact]
    public void TranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_cli.TranslateToolName("SomeUnknownTool"));
    }

    [Fact]
    public void ReverseTranslateToolName_KnownTool_ReturnsCanonical()
    {
        Assert.Equal(CanonicalTools.Read, _cli.ReverseTranslateToolName("Read"));
        Assert.Equal(CanonicalTools.Bash, _cli.ReverseTranslateToolName("Bash"));
    }

    [Fact]
    public void ReverseTranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_cli.ReverseTranslateToolName("Agent"));
    }

    [Fact]
    public void ExtractWritableDirectories_ReturnsEmpty()
    {
        var dirs = _cli.ExtractWritableDirectories(["Read", "Write"]);
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

        Assert.Equal("claude", spec.FileName);
        Assert.Contains("--print", spec.Arguments);
        Assert.Contains("--verbose", spec.Arguments);
        Assert.Contains("--output-format", spec.Arguments);
        Assert.Contains("stream-json", spec.Arguments);
        Assert.Contains("--permission-mode", spec.Arguments);
        Assert.Contains("dontAsk", spec.Arguments);
        Assert.Contains("-", spec.Arguments);
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
            Model = "claude-sonnet-4-6",
        };

        var spec = _cli.BuildProcessSpec(config);

        var modelIdx = spec.Arguments.ToList().IndexOf("--model");
        Assert.True(modelIdx >= 0);
        Assert.Equal("claude-sonnet-4-6", spec.Arguments[modelIdx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithEffort_IncludesEffortFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            Effort = EffortLevel.High,
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--effort");
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
        var idx = spec.Arguments.ToList().IndexOf("--effort");
        Assert.Equal(expected, spec.Arguments[idx + 1]);
    }

    [Theory]
    [InlineData(PermissionMode.FullAuto, "dontAsk")]
    [InlineData(PermissionMode.AcceptEdits, "acceptEdits")]
    [InlineData(PermissionMode.Plan, "plan")]
    [InlineData(PermissionMode.Default, "default")]
    public void BuildProcessSpec_AllPermissionModes_MapCorrectly(PermissionMode mode, string expected)
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            PermissionMode = mode,
        };

        var spec = _cli.BuildProcessSpec(config);
        var idx = spec.Arguments.ToList().IndexOf("--permission-mode");
        Assert.Equal(expected, spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithSessionId_IncludesSessionIdFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            SessionId = "my-session-id",
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--session-id");
        Assert.True(idx >= 0);
        Assert.Equal("my-session-id", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithAllowedTools_IncludesAllowedToolsFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = ["Read", "Write"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--allowedTools");
        Assert.True(idx >= 0);
        Assert.Equal("Read Write", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithMaxTurns_IncludesMaxTurnsFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            MaxTurns = 3,
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--max-turns");
        Assert.True(idx >= 0);
        Assert.Equal("3", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithSystemPrompt_IncludesFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            SystemPrompt = "You are a helpful assistant",
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--system-prompt");
        Assert.True(idx >= 0);
        Assert.Equal("You are a helpful assistant", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithMcpServers_IncludesFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            McpServers = [new McpServerConfig("github", "gh", ["mcp-server"])],
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--mcp-server");
        Assert.True(idx >= 0);
        Assert.Equal("github", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithExtraArgs_AppendsThemBeforeDash()
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
        Assert.Equal("-", spec.Arguments[^1]);
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
    public void BuildProcessSpec_NoEffort_DoesNotIncludeEffortFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.DoesNotContain("--effort", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_NoSessionId_DoesNotIncludeSessionIdFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.DoesNotContain("--session-id", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_EmptyAllowedTools_DoesNotIncludeFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = [],
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.DoesNotContain("--allowedTools", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_PathWithSpaces_PreservesPath()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/path/with spaces/and more spaces",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("/path/with spaces/and more spaces", spec.WorkingDirectory);
    }

    [Fact]
    public void BuildProcessSpec_PromptWithSpecialChars_PreservesPrompt()
    {
        var prompt = "What's the result of `echo \"hello world\"` && echo 'done'?";
        var config = new AgentLaunchConfig
        {
            Prompt = prompt,
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal(prompt, spec.StdinContent);
    }

    [Fact]
    public void BuildProcessSpec_MultilinePrompt_PreservesNewlines()
    {
        var prompt = "Line 1\nLine 2\nLine 3";
        var config = new AgentLaunchConfig
        {
            Prompt = prompt,
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal(prompt, spec.StdinContent);
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
