using System;
using System.Collections.Generic;
using System.IO;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Helpers;

[Collection("TendrilHome")]
public class AgentLaunchHelperTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("agent-launch-helper-tests");

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public void GetDefaultWorkDir_ReturnsTendrilHome_WhenConfigured()
    {
        var config = new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path);
        var dir = AgentLaunchHelper.GetDefaultWorkDir(config);
        Assert.Equal(_tempDir.Path, dir);
    }

    [Fact]
    public void GetDefaultWorkDir_ReturnsUserProfile_WhenTendrilHomeEmpty()
    {
        var config = new TestPlanConfigService(_tempDir.Path, tendrilHome: "");
        var dir = AgentLaunchHelper.GetDefaultWorkDir(config);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), dir);
    }

    [Fact]
    public void CompileSystemPrompt_ReturnsValidPrompt()
    {
        var config = new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path);
        var prompt = AgentLaunchHelper.CompileSystemPrompt(config);
        Assert.NotNull(prompt);
        Assert.Contains(config.TendrilHome.Replace('\\', '/'), prompt);
    }

    [Fact]
    public void WriteAgentInstructionsIfNeeded_WritesAgentsMd_ForAntigravity()
    {
        var runner = TestAgentRunner.Create();
        var workDir = _tempDir.Path;
        var instructions = "# Test System Prompt";

        AgentLaunchHelper.WriteAgentInstructionsIfNeeded(workDir, instructions, runner, "antigravity");

        var expectedFile = Path.Combine(workDir, "AGENTS.md");
        Assert.True(File.Exists(expectedFile));
        Assert.Equal(instructions, File.ReadAllText(expectedFile));
    }

    [Fact]
    public void WriteAgentInstructionsIfNeeded_Skips_ForClaude()
    {
        var runner = TestAgentRunner.Create();
        var workDir = Path.Combine(_tempDir.Path, "claude-test");
        Directory.CreateDirectory(workDir);
        var instructions = "# Test System Prompt";

        AgentLaunchHelper.WriteAgentInstructionsIfNeeded(workDir, instructions, runner, "claude");

        Assert.False(File.Exists(Path.Combine(workDir, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(workDir, "GEMINI.md")));
    }

    [Fact]
    public void GetEnvironment_IncludesAgentSpecificEnvAndTendrilEnv()
    {
        var config = new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path);
        config.Settings.CodingAgents =
        [
            new AgentConfig
            {
                Name = "claude",
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["CUSTOM_KEY"] = "custom_val",
                    ["ANTHROPIC_BASE_URL"] = "https://api.example.com"
                }
            }
        ];

        var env = AgentLaunchHelper.GetEnvironment(config, "claude");

        Assert.Equal("custom_val", env["CUSTOM_KEY"]);
        Assert.Equal("https://api.example.com", env["ANTHROPIC_BASE_URL"]);
        Assert.Equal(_tempDir.Path, env["TENDRIL_HOME"]);
        Assert.True(env.ContainsKey("PATH"));
    }

    [Fact]
    public void ResolveModel_HonorsExplicitRequestedModel()
    {
        var config = new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path);
        var runner = TestAgentRunner.Create();

        var model = AgentLaunchHelper.ResolveModel(config, runner, "claude", "claude-3-opus");
        Assert.Equal("claude-3-opus", model);
    }

    [Fact]
    public void ResolveModel_ReturnsDefault_ForClaude()
    {
        var config = new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path);
        var runner = TestAgentRunner.Create();

        var model = AgentLaunchHelper.ResolveModel(config, runner, "claude", "default");
        Assert.Equal("default", model);
    }

    [Fact]
    public void ResolveModel_ReturnsKimiK3_ForBerget()
    {
        var config = new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path);
        var runner = TestAgentRunner.Create();

        var model = AgentLaunchHelper.ResolveModel(config, runner, "berget", "default");
        Assert.Equal("moonshotai/Kimi-K3", model);
    }

    [Fact]
    public void PrepareResolutionContext_ConstructsCompleteContext()
    {
        var config = new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path);
        var runner = TestAgentRunner.Create();

        var context = AgentLaunchHelper.PrepareResolutionContext(
            config,
            runner,
            "antigravity",
            "Do a task",
            modelOverride: "default",
            permissionMode: PermissionMode.FullAuto
        );

        Assert.Equal("antigravity", context.AgentId);
        Assert.Equal("Do a task", context.Prompt);
        Assert.NotNull(context.SystemPrompt);
        Assert.Equal(_tempDir.Path, context.WorkingDirectory);
        Assert.Equal(PermissionMode.FullAuto, context.PermissionMode);
        Assert.NotNull(context.ExtraEnvironment);
        Assert.True(context.ExtraEnvironment.ContainsKey("TENDRIL_HOME"));

        // AGENTS.md should be written to the working directory for Antigravity
        var agentsMd = Path.Combine(_tempDir.Path, "AGENTS.md");
        Assert.True(File.Exists(agentsMd));
    }
}
