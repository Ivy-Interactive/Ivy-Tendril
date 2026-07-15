using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Copilot;

namespace Ivy.Tendril.Agents.Test.Copilot;

public class CopilotCliTests
{
    private readonly CopilotCli _cli = new();

    [Fact]
    public void Id_IsCopilot()
    {
        Assert.Equal(AgentId.Copilot, _cli.Id);
    }

    [Fact]
    public void DisplayName_IsGitHubCopilot()
    {
        Assert.Equal("Copilot", _cli.DisplayName);
    }

    [Fact]
    public void Capabilities_DoesNotIncludeArgumentPrompt()
    {
        Assert.False(_cli.Capabilities.HasFlag(AgentCapabilities.ArgumentPrompt));
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
    public void Capabilities_IncludesEffortControl()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.EffortControl));
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
    public void TranslateToolName_Read_ReturnsView()
    {
        Assert.Equal("view", _cli.TranslateToolName(CanonicalTools.Read));
    }

    [Fact]
    public void TranslateToolName_Write_ReturnsApplyPatch()
    {
        Assert.Equal("apply_patch", _cli.TranslateToolName(CanonicalTools.Write));
    }

    [Fact]
    public void TranslateToolName_Edit_ReturnsApplyPatch()
    {
        Assert.Equal("apply_patch", _cli.TranslateToolName(CanonicalTools.Edit));
    }

    [Fact]
    public void TranslateToolName_Bash_ReturnsPlatformSpecific()
    {
        var expected = OperatingSystem.IsWindows() ? "powershell" : "bash";
        Assert.Equal(expected, _cli.TranslateToolName(CanonicalTools.Bash));
    }

    [Fact]
    public void TranslateToolName_Glob_ReturnsGlob()
    {
        Assert.Equal("glob", _cli.TranslateToolName(CanonicalTools.Glob));
    }

    [Fact]
    public void TranslateToolName_Grep_ReturnsRg()
    {
        Assert.Equal("rg", _cli.TranslateToolName(CanonicalTools.Grep));
    }

    [Fact]
    public void TranslateToolName_WebFetch_ReturnsWebFetch()
    {
        Assert.Equal("web_fetch", _cli.TranslateToolName(CanonicalTools.WebFetch));
    }

    [Fact]
    public void TranslateToolName_WebSearch_ReturnsWebFetch()
    {
        Assert.Equal("web_fetch", _cli.TranslateToolName(CanonicalTools.WebSearch));
    }

    [Fact]
    public void TranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_cli.TranslateToolName("SomeUnknownTool"));
    }

    [Fact]
    public void ReverseTranslateToolName_View_ReturnsRead()
    {
        Assert.Equal(CanonicalTools.Read, _cli.ReverseTranslateToolName("view"));
    }

    [Fact]
    public void ReverseTranslateToolName_ApplyPatch_ReturnsFirstWins()
    {
        // First entry for apply_patch is Write (since Write appears before Edit in the dictionary)
        var result = _cli.ReverseTranslateToolName("apply_patch");
        Assert.Equal(CanonicalTools.Write, result);
    }

    [Fact]
    public void ReverseTranslateToolName_Glob_ReturnsGlob()
    {
        Assert.Equal(CanonicalTools.Glob, _cli.ReverseTranslateToolName("glob"));
    }

    [Fact]
    public void ReverseTranslateToolName_Rg_ReturnsGrep()
    {
        Assert.Equal(CanonicalTools.Grep, _cli.ReverseTranslateToolName("rg"));
    }

    [Fact]
    public void ReverseTranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_cli.ReverseTranslateToolName("unknown_native_tool"));
    }

    [Fact]
    public void ExtractWritableDirectories_WriteScopedWithGlob_ExtractsDirectory()
    {
        var dirs = _cli.ExtractWritableDirectories(["Write:/plans/**"]);
        Assert.Single(dirs);
        Assert.Equal("/plans", dirs[0]);
    }

    [Fact]
    public void ExtractWritableDirectories_ApplyPatchScopedWithGlob_ExtractsDirectory()
    {
        var dirs = _cli.ExtractWritableDirectories(["apply_patch=/inbox/*"]);
        Assert.Single(dirs);
        Assert.Equal("/inbox", dirs[0]);
    }

    [Fact]
    public void ExtractWritableDirectories_UnscopedTools_ReturnsEmpty()
    {
        var dirs = _cli.ExtractWritableDirectories(["Read", "Bash", "Glob"]);
        Assert.Empty(dirs);
    }

    [Fact]
    public void BuildProcessSpec_MinimalConfig_ProducesCorrectArgs()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello world",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.True(spec.FileName == "copilot" || spec.FileName == "gh",
            $"Expected 'copilot' or 'gh', got '{spec.FileName}'");
        Assert.DoesNotContain("-p", spec.Arguments);
        Assert.DoesNotContain("Hello world", spec.Arguments);
        Assert.Contains("--allow-all-paths", spec.Arguments);
        Assert.Contains("--allow-all-urls", spec.Arguments);
        Assert.Contains("--output-format", spec.Arguments);
        Assert.Contains("json", spec.Arguments);
        Assert.Contains("-s", spec.Arguments);
        Assert.Equal("Hello world", spec.StdinContent);
        Assert.True(spec.RedirectStdin);
        Assert.Equal("/tmp", spec.WorkingDirectory);
    }

    [Fact]
    public void BuildProcessSpec_WithModel_IncludesModelFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            Model = "gpt-4o",
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--model");
        Assert.True(idx >= 0);
        Assert.Equal("gpt-4o", spec.Arguments[idx + 1]);
    }

    [Theory]
    [InlineData(EffortLevel.Low, "low")]
    [InlineData(EffortLevel.Medium, "medium")]
    [InlineData(EffortLevel.High, "high")]
    [InlineData(EffortLevel.XHigh, "xhigh")]
    [InlineData(EffortLevel.Max, "xhigh")]
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
        Assert.True(idx >= 0);
        Assert.Equal(expected, spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithSession_IncludesNameFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            SessionId = "my-session",
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--name");
        Assert.True(idx >= 0);
        Assert.Equal("my-session", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithAllowedTools_IncludesAvailableToolsAndAllowAllTools()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = [CanonicalTools.Read, CanonicalTools.Bash],
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--available-tools");
        Assert.True(idx >= 0);
        var toolsValue = spec.Arguments[idx + 1];
        // Should be translated comma-separated names
        Assert.Contains("view", toolsValue);
        var shellTool = OperatingSystem.IsWindows() ? "powershell" : "bash";
        Assert.Contains(shellTool, toolsValue);
        Assert.Contains("--allow-all-tools", spec.Arguments);
    }

    [Fact]
    public void BuildProcessSpec_WithWritableDirs_IncludesAddDirFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            WritableDirectories = ["/data", "/output"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var args = spec.Arguments.ToList();
        var firstIdx = args.IndexOf("--add-dir");
        Assert.True(firstIdx >= 0);
        Assert.Equal("/data", args[firstIdx + 1]);

        var secondIdx = args.IndexOf("--add-dir", firstIdx + 1);
        Assert.True(secondIdx >= 0);
        Assert.Equal("/output", args[secondIdx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithExtraArgs_AppendsAtEnd()
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
