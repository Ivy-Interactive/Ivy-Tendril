using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Test.Gemini;

public class GeminiCliTests
{
    private readonly GeminiCli _cli = new();

    [Fact]
    public void Id_IsGemini()
    {
        Assert.Equal(AgentId.Gemini, _cli.Id);
    }

    [Fact]
    public void DisplayName_IsGeminiCli()
    {
        Assert.Equal("Gemini CLI", _cli.DisplayName);
    }

    [Fact]
    public void Capabilities_IncludesStdinPrompt()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.StdinPrompt));
    }

    [Fact]
    public void Capabilities_IncludesJsonOutput()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.JsonOutput));
    }

    [Fact]
    public void Capabilities_IncludesModelSelection()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.ModelSelection));
    }

    [Fact]
    public void Capabilities_IncludesDirectoryRestriction()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.DirectoryRestriction));
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
    public void Capabilities_IncludesSessionResume()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.SessionResume));
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
    public void PreferredOutputFormat_IsJson()
    {
        Assert.Equal(OutputFormat.Json, _cli.PreferredOutputFormat);
    }

    [Fact]
    public void TranslateToolName_AnyTool_ReturnsNull()
    {
        Assert.Null(_cli.TranslateToolName(CanonicalTools.Read));
        Assert.Null(_cli.TranslateToolName(CanonicalTools.Bash));
        Assert.Null(_cli.TranslateToolName(CanonicalTools.WebFetch));
        Assert.Null(_cli.TranslateToolName("SomeUnknownTool"));
    }

    [Fact]
    public void ReverseTranslateToolName_AnyTool_ReturnsNull()
    {
        Assert.Null(_cli.ReverseTranslateToolName("Read"));
        Assert.Null(_cli.ReverseTranslateToolName("Bash"));
        Assert.Null(_cli.ReverseTranslateToolName("SomeTool"));
    }

    [Theory]
    [InlineData("dir:/plans", "/plans")]
    [InlineData("dir:D:\\Plans\\00123", "D:\\Plans\\00123")]
    public void ExtractWritableDirectories_DirPrefix_ExtractsPath(string tool, string expected)
    {
        var dirs = _cli.ExtractWritableDirectories([tool]);
        Assert.Single(dirs);
        Assert.Equal(expected, dirs[0]);
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    [InlineData("Bash")]
    [InlineData("notadir:something")]
    public void ExtractWritableDirectories_NonDirTool_ReturnsEmpty(string tool)
    {
        var dirs = _cli.ExtractWritableDirectories([tool]);
        Assert.Empty(dirs);
    }

    [Fact]
    public void ExtractWritableDirectories_MultipleDirs_ExtractsAll()
    {
        var dirs = _cli.ExtractWritableDirectories(["dir:/plans", "Read", "dir:/output"]);
        Assert.Equal(2, dirs.Count);
        Assert.Contains("/plans", dirs);
        Assert.Contains("/output", dirs);
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

        Assert.Equal("gemini", spec.FileName);
        Assert.Contains("--approval-mode", spec.Arguments);
        Assert.Contains("yolo", spec.Arguments);
        Assert.Contains("--skip-trust", spec.Arguments);
        Assert.Contains("--output-format", spec.Arguments);
        Assert.Contains("json", spec.Arguments);
        Assert.Contains("--prompt", spec.Arguments);
        Assert.Contains(" ", spec.Arguments);
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
            Model = "gemini-2.5-pro",
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--model");
        Assert.True(idx >= 0);
        Assert.Equal("gemini-2.5-pro", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithSession_IncludesResumeFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            SessionId = "session-abc-123",
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--resume");
        Assert.True(idx >= 0);
        Assert.Equal("session-abc-123", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithWritableDirectories_IncludesIncludeDirectories()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = ["dir:/plans", "dir:/output"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var args = spec.Arguments.ToList();
        var indices = args.Select((a, i) => (a, i))
            .Where(x => x.a == "--include-directories")
            .Select(x => x.i)
            .ToList();

        Assert.Equal(2, indices.Count);
        Assert.Contains("/plans", spec.Arguments);
        Assert.Contains("/output", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_WithExtraArgs_AppendsBeforePrompt()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            ExtraArguments = ["--custom", "value"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var args = spec.Arguments.ToList();
        var customIdx = args.IndexOf("--custom");
        var promptIdx = args.IndexOf("--prompt");
        Assert.True(customIdx >= 0);
        Assert.True(promptIdx >= 0);
        Assert.True(customIdx < promptIdx);
        Assert.Equal("value", spec.Arguments[customIdx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_AllowedToolsWithWrite_ApprovalModeIsYolo()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = ["Read", "Write", "Bash"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--approval-mode");
        Assert.Equal("yolo", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_AllowedToolsWithEdit_ApprovalModeIsYolo()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = ["Read", "Edit"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--approval-mode");
        Assert.Equal("yolo", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_AllowedToolsWithoutWriteOrEdit_ApprovalModeIsPlan()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = ["Read", "Bash", "Grep"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--approval-mode");
        Assert.Equal("plan", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_EmptyAllowedTools_ApprovalModeIsYolo()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = [],
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--approval-mode");
        Assert.Equal("yolo", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_Environment_ContainsCiTrue()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("true", spec.Environment["CI"]);
    }

    [Fact]
    public void BuildProcessSpec_Environment_ContainsTermDumb()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("dumb", spec.Environment["TERM"]);
    }

    [Fact]
    public void BuildProcessSpec_Environment_ContainsGeminiTrustWorkspace()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("true", spec.Environment["GEMINI_CLI_TRUST_WORKSPACE"]);
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
        Assert.Equal("dumb", spec.Environment["TERM"]);
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
    public void BuildProcessSpec_NoSession_DoesNotIncludeResumeFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.DoesNotContain("--resume", spec.Arguments);
    }

    [Fact]
    public void GetDefaultEnvironment_ContainsAllThreeVars()
    {
        var env = _cli.GetDefaultEnvironment();

        Assert.Equal(3, env.Count);
        Assert.Equal("true", env["CI"]);
        Assert.Equal("dumb", env["TERM"]);
        Assert.Equal("true", env["GEMINI_CLI_TRUST_WORKSPACE"]);
    }
}
