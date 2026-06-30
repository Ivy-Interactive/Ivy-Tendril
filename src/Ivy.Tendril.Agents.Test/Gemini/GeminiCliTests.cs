using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Test.Gemini;

public class GeminiCliTests
{
    private readonly GeminiCli _cli = new();

    [Fact]
    public void Id_IsGemini()
    {
        Assert.Equal("gemini", _cli.Id);
    }

    [Fact]
    public void DisplayName_IsGeminiCli()
    {
        Assert.Equal("Gemini", _cli.DisplayName);
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
    public void Capabilities_IncludesDirectoryRestriction()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.DirectoryRestriction));
    }

    [Fact]
    public void Capabilities_DoesNotIncludeSessionResume()
    {
        Assert.False(_cli.Capabilities.HasFlag(AgentCapabilities.SessionResume));
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

    [Fact]
    public void DefaultProfiles_HasThreeEntries()
    {
        Assert.Equal(3, _cli.DefaultProfiles.Count);
    }

    [Fact]
    public void DefaultProfiles_DeepHasNoModel()
    {
        var deep = _cli.DefaultProfiles.First(p => p.Tier == ProfileTier.Deep);
        Assert.Null(deep.Model);
        Assert.Null(deep.Effort);
    }

    [Fact]
    public void DefaultProfiles_BalancedHasNoModel()
    {
        var balanced = _cli.DefaultProfiles.First(p => p.Tier == ProfileTier.Balanced);
        Assert.Null(balanced.Model);
    }

    [Fact]
    public void DefaultProfiles_QuickHasNoModel()
    {
        var quick = _cli.DefaultProfiles.First(p => p.Tier == ProfileTier.Quick);
        Assert.Null(quick.Model);
    }

    [Fact]
    public void TranslateToolName_ReturnsNull()
    {
        Assert.Null(_cli.TranslateToolName("Read"));
        Assert.Null(_cli.TranslateToolName("Bash"));
    }

    [Fact]
    public void ReverseTranslateToolName_ReturnsNull()
    {
        Assert.Null(_cli.ReverseTranslateToolName("read_file"));
    }

    [Fact]
    public void ExtractWritableDirectories_ExtractsDirPrefix()
    {
        var tools = new List<string> { "Read", "dir:/tmp/workspace", "Bash", "dir:/home/user" };
        var dirs = _cli.ExtractWritableDirectories(tools);

        Assert.Equal(2, dirs.Count);
        Assert.Contains("/tmp/workspace", dirs);
        Assert.Contains("/home/user", dirs);
    }

    [Fact]
    public void ExtractWritableDirectories_EmptyWhenNoDirTools()
    {
        var tools = new List<string> { "Read", "Write", "Bash" };
        var dirs = _cli.ExtractWritableDirectories(tools);
        Assert.Empty(dirs);
    }

    [Fact]
    public void BuildProcessSpec_BasicInvocation_HasCorrectFileName()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);
        Assert.Equal("gemini", spec.FileName);
    }

    [Fact]
    public void BuildProcessSpec_BasicInvocation_IncludesStreamJsonFormat()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);
        var args = spec.Arguments.ToList();
        var idx = args.IndexOf("--output-format");
        Assert.True(idx >= 0);
        Assert.Equal("stream-json", args[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_BasicInvocation_IncludesSkipTrust()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);
        Assert.Contains("--skip-trust", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_FullAutoMode_UsesYoloApproval()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            PermissionMode = PermissionMode.FullAuto,
        };

        var spec = _cli.BuildProcessSpec(config);
        var args = spec.Arguments.ToList();
        var idx = args.IndexOf("--approval-mode");
        Assert.Equal("yolo", args[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_PlanMode_UsesPlanApproval()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            PermissionMode = PermissionMode.Plan,
        };

        var spec = _cli.BuildProcessSpec(config);
        var args = spec.Arguments.ToList();
        var idx = args.IndexOf("--approval-mode");
        Assert.Equal("plan", args[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithModel_IncludesModelFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            Model = "gemini-2.5-pro",
        };

        var spec = _cli.BuildProcessSpec(config);
        var args = spec.Arguments.ToList();
        var idx = args.IndexOf("--model");
        Assert.True(idx >= 0);
        Assert.Equal("gemini-2.5-pro", args[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithSessionId_DoesNotPassResumeFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            SessionId = "session-123",
        };

        var spec = _cli.BuildProcessSpec(config);
        Assert.DoesNotContain("--resume", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_WithDirectories_IncludesIncludeDirectoriesFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            WritableDirectories = ["/home/user/project"],
        };

        var spec = _cli.BuildProcessSpec(config);
        var args = spec.Arguments.ToList();
        var idx = args.IndexOf("--include-directories");
        Assert.True(idx >= 0);
        Assert.Equal("/home/user/project", args[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_IncludesHeadlessPromptFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);
        var args = spec.Arguments.ToList();
        var idx = args.IndexOf("--prompt");
        Assert.True(idx >= 0);
        Assert.Equal(" ", args[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_SetsStdinContent()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Fix the bug",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);
        Assert.Equal("Fix the bug", spec.StdinContent);
        Assert.True(spec.RedirectStdin);
    }

    [Fact]
    public void BuildProcessSpec_Environment_IncludesGeminiTrustWorkspace()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);
        Assert.Equal("true", spec.Environment["GEMINI_CLI_TRUST_WORKSPACE"]);
        Assert.Equal("true", spec.Environment["CI"]);
        Assert.Equal("dumb", spec.Environment["TERM"]);
    }

    [Fact]
    public void BuildProcessSpec_ExtraArguments_AreAppended()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            ExtraArguments = ["--custom", "value"],
        };

        var spec = _cli.BuildProcessSpec(config);
        Assert.Contains("--custom", spec.Arguments);
        Assert.Contains("value", spec.Arguments);
    }

    [Fact]
    public void GetDefaultEnvironment_ContainsExpectedKeys()
    {
        var env = _cli.GetDefaultEnvironment();
        Assert.Equal("true", env["CI"]);
        Assert.Equal("dumb", env["TERM"]);
        Assert.Equal("true", env["GEMINI_CLI_TRUST_WORKSPACE"]);
    }
}
