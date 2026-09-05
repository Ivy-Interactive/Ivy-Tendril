using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Codex;

namespace Ivy.Tendril.Agents.Test.Codex;

public class CodexCliTests
{
    private readonly CodexCli _cli = new();

    [Fact]
    public void Id_IsCodex()
    {
        Assert.Equal("codex", _cli.Id);
    }

    [Fact]
    public void DisplayName_IsCodex()
    {
        Assert.Equal("Codex", _cli.DisplayName);
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
    public void Capabilities_IncludesHealthCheck()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.HealthCheck));
    }

    [Fact]
    public void Capabilities_IncludesEffortControl()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.EffortControl));
    }

    [Fact]
    public void Capabilities_IncludesExtraArgPassthrough()
    {
        Assert.True(_cli.Capabilities.HasFlag(AgentCapabilities.ExtraArgPassthrough));
    }

    [Fact]
    public void SupportedEfforts_ReturnsCodexEffortLevels()
    {
        Assert.Equal(EffortLevels.Codex, _cli.SupportedEfforts);
    }

    [Fact]
    public void DefaultProfiles_MatchesConfiguredModels()
    {
        Assert.Collection(_cli.DefaultProfiles,
            p =>
            {
                Assert.Equal(ProfileTier.Deep, p.Tier);
                Assert.Equal("gpt-5.6-sol", p.Model);
                Assert.Equal("high", p.Effort);
            },
            p =>
            {
                Assert.Equal(ProfileTier.Balanced, p.Tier);
                Assert.Equal("gpt-5.6-terra", p.Model);
                Assert.Equal("medium", p.Effort);
            },
            p =>
            {
                Assert.Equal(ProfileTier.Quick, p.Tier);
                Assert.Equal("gpt-5.6-luna", p.Model);
                Assert.Equal("low", p.Effort);
            });
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
    public void TranslateToolName_Bash_ReturnsBash()
    {
        Assert.Equal("bash", _cli.TranslateToolName(CanonicalTools.Bash));
    }

    [Fact]
    public void TranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_cli.TranslateToolName("SomeUnknownTool"));
    }

    [Fact]
    public void ReverseTranslateToolName_Bash_ReturnsBash()
    {
        Assert.Equal(CanonicalTools.Bash, _cli.ReverseTranslateToolName("bash"));
    }

    [Fact]
    public void ReverseTranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_cli.ReverseTranslateToolName("unknown_tool"));
    }

    [Fact]
    public void ExtractWritableDirectories_WriteWithGlobstar_ExtractsDir()
    {
        var dirs = _cli.ExtractWritableDirectories(["Write(/plans/**)"]);
        Assert.Single(dirs);
        Assert.Equal("/plans", dirs[0]);
    }

    [Fact]
    public void ExtractWritableDirectories_EditWithNestedGlobstar_ExtractsDir()
    {
        var dirs = _cli.ExtractWritableDirectories(["Edit(/plans/01234/**)"]);
        Assert.Single(dirs);
        Assert.Equal("/plans/01234", dirs[0]);
    }

    [Fact]
    public void ExtractWritableDirectories_WriteWithSingleStar_ExtractsDir()
    {
        var dirs = _cli.ExtractWritableDirectories(["Write(/inbox/*)"]);
        Assert.Single(dirs);
        Assert.Equal("/inbox", dirs[0]);
    }

    [Fact]
    public void ExtractWritableDirectories_BackslashPath_ExtractsDir()
    {
        var dirs = _cli.ExtractWritableDirectories([@"Write(D:\Plans\00123\**)"]);
        Assert.Single(dirs);
        Assert.Equal(@"D:\Plans\00123", dirs[0]);
    }

    [Fact]
    public void ExtractWritableDirectories_UnscopedWrite_ReturnsEmpty()
    {
        var dirs = _cli.ExtractWritableDirectories(["Write"]);
        Assert.Empty(dirs);
    }

    [Fact]
    public void ExtractWritableDirectories_DuplicateWriteAndEdit_Deduplicates()
    {
        var dirs = _cli.ExtractWritableDirectories(["Write(/x/**)", "Edit(/x/**)"]);
        // Both resolve to /x — ExtractWritableDirectories returns both, dedup happens in BuildProcessSpec
        Assert.All(dirs, d => Assert.Equal("/x", d));
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

        Assert.Equal("codex", spec.FileName);
        Assert.Contains("exec", spec.Arguments);
        Assert.Contains("--sandbox", spec.Arguments);
        Assert.Contains("danger-full-access", spec.Arguments);
        Assert.Contains("approval_policy=\"never\"", spec.Arguments);
        Assert.Contains("--json", spec.Arguments);
        Assert.Contains("--skip-git-repo-check", spec.Arguments);
        Assert.Contains("-", spec.Arguments);
        Assert.Equal("Hello", spec.StdinContent);
        Assert.Equal("/tmp", spec.WorkingDirectory);
        Assert.True(spec.RedirectStdin);
    }

    [Fact]
    public void BuildProcessSpec_PermissionMode_FullAuto_SetsCorrectSandboxAndPermissions()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            PermissionMode = PermissionMode.FullAuto,
        };

        var spec = _cli.BuildProcessSpec(config);

        var argList = spec.Arguments.ToList();
        var sandboxIdx = argList.IndexOf("--sandbox");
        Assert.True(sandboxIdx >= 0);
        Assert.Equal("danger-full-access", argList[sandboxIdx + 1]);

        var approvalIdx = argList.IndexOf("approval_policy=\"never\"");
        Assert.True(approvalIdx >= 0);
    }

    [Fact]
    public void BuildProcessSpec_PermissionMode_AcceptEdits_SetsWorkspaceWriteWithDiskPermissions()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            PermissionMode = PermissionMode.AcceptEdits,
        };

        var spec = _cli.BuildProcessSpec(config);

        var argList = spec.Arguments.ToList();
        var sandboxIdx = argList.IndexOf("--sandbox");
        Assert.True(sandboxIdx >= 0);
        Assert.Equal("workspace-write", argList[sandboxIdx + 1]);

        Assert.Contains("sandbox_workspace_write.network_access=true", argList);
        Assert.Contains("sandbox_permissions=[\"disk-full-read-access\"]", argList);

        var approvalIdx = argList.IndexOf("approval_policy=\"never\"");
        Assert.True(approvalIdx >= 0);
    }

    [Fact]
    public void BuildProcessSpec_PermissionMode_Plan_SetsReadOnlySandbox()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            PermissionMode = PermissionMode.Plan,
        };

        var spec = _cli.BuildProcessSpec(config);

        var argList = spec.Arguments.ToList();
        var sandboxIdx = argList.IndexOf("--sandbox");
        Assert.True(sandboxIdx >= 0);
        Assert.Equal("read-only", argList[sandboxIdx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_PermissionMode_Default_SetsWorkspaceWrite()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            PermissionMode = PermissionMode.Default,
        };

        var spec = _cli.BuildProcessSpec(config);

        var argList = spec.Arguments.ToList();
        var sandboxIdx = argList.IndexOf("--sandbox");
        Assert.True(sandboxIdx >= 0);
        Assert.Equal("workspace-write", argList[sandboxIdx + 1]);

        Assert.Contains("sandbox_workspace_write.network_access=true", argList);
    }

    [Fact]
    public void BuildProcessSpec_EnablesNetworkAccessInWorkspaceWriteSandbox()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello",
            WorkingDirectory = "/tmp",
            PermissionMode = PermissionMode.Default,
        };

        var spec = _cli.BuildProcessSpec(config);

        var argList = spec.Arguments.ToList();
        var configIdx = argList.IndexOf("-c");
        Assert.True(configIdx >= 0);
        Assert.Equal("sandbox_workspace_write.network_access=true", argList[configIdx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithModel_IncludesModelFlag()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            Model = "o4-mini",
        };

        var spec = _cli.BuildProcessSpec(config);

        var modelIdx = spec.Arguments.ToList().IndexOf("--model");
        Assert.True(modelIdx >= 0);
        Assert.Equal("o4-mini", spec.Arguments[modelIdx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithEffort_IncludesReasoningEffortConfig()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            Effort = EffortLevel.High,
        };

        var spec = _cli.BuildProcessSpec(config);

        var argList = spec.Arguments.ToList();
        var idx = argList.IndexOf("-c");
        Assert.True(idx >= 0);
        Assert.Contains(argList, a => a == "model_reasoning_effort=\"high\"");
    }

    [Fact]
    public void BuildProcessSpec_NoEffort_DoesNotIncludeReasoningEffortConfig()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.DoesNotContain(spec.Arguments, a => a.StartsWith("model_reasoning_effort"));
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

        var argList = spec.Arguments.ToList();
        var expectedArg = $"model_reasoning_effort=\"{expected}\"";
        Assert.Contains(expectedArg, argList);
        var idx = argList.IndexOf(expectedArg);
        Assert.True(idx > 0);
        Assert.Equal("-c", argList[idx - 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithWritableDirsFromAllowedTools_IncludesAddDir()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = ["Write(/plans/**)"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--add-dir");
        Assert.True(idx >= 0);
        Assert.Equal("/plans", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithWritableDirectoriesConfig_IncludesAddDir()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            WritableDirectories = ["/output"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var idx = spec.Arguments.ToList().IndexOf("--add-dir");
        Assert.True(idx >= 0);
        Assert.Equal("/output", spec.Arguments[idx + 1]);
    }

    [Fact]
    public void BuildProcessSpec_WithExtraArgs_AppendsBeforeDash()
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
        var customIdx = spec.Arguments.ToList().IndexOf("--custom");
        var dashIdx = spec.Arguments.ToList().IndexOf("-");
        Assert.True(customIdx < dashIdx);
    }

    [Fact]
    public void BuildProcessSpec_StdinMarkerAlwaysLast()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            Model = "o3",
            ExtraArguments = ["--some-flag"],
            WritableDirectories = ["/data"],
        };

        var spec = _cli.BuildProcessSpec(config);

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
    public void GetDefaultEnvironment_ContainsCiAndTerm()
    {
        var env = _cli.GetDefaultEnvironment();

        Assert.Equal("true", env["CI"]);
        Assert.Equal("dumb", env["TERM"]);
    }

    [Fact]
    public void BuildProcessSpec_DuplicateWritableDirectories_Deduplicates()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            AllowedTools = ["Write(/x/**)", "Edit(/x/**)"],
            WritableDirectories = ["/x"],
        };

        var spec = _cli.BuildProcessSpec(config);

        var argList = spec.Arguments.ToList();
        var addDirCount = argList.Count(a => a == "--add-dir");
        Assert.Equal(1, addDirCount);
    }
}
