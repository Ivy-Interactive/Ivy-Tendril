using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Test.Agents;

public class AgentProviderFactoryTests
{
    private static IAgentRunner CreateRunner() => TestAgentRunner.Create();

    private static TendrilSettings CreateSettings(
        string codingAgent = "claude",
        Dictionary<string, PromptwareConfig>? promptwares = null,
        List<AgentConfig>? codingAgents = null)
    {
        return new TendrilSettings
        {
            CodingAgent = codingAgent,
            Promptwares = promptwares ?? new Dictionary<string, PromptwareConfig>(),
            CodingAgents = codingAgents ?? new List<AgentConfig>()
        };
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("antigravity")]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void GetCli_ReturnsCorrectAgent(string name)
    {
        var runner = CreateRunner();
        var cli = runner.GetCli(name);
        Assert.Equal(name, cli.Id);
    }

    [Fact]
    public void GetCli_CaseInsensitive()
    {
        var runner = CreateRunner();
        var cli = runner.GetCli("Claude");
        Assert.Equal("claude", cli.Id);
    }

    [Fact]
    public void GetCli_ThrowsForUnknown()
    {
        var runner = CreateRunner();
        Assert.Throws<ArgumentException>(() => runner.GetCli("unknown-agent"));
    }

    [Fact]
    public void Resolve_UsesDefaultProfile()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new() { Profile = "balanced", AllowedTools = new List<string> { "Write" } }
            },
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "claude",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "balanced", Model = "sonnet", Effort = "high" }
                    }
                }
            });

        var jobContext = new Dictionary<string, string>
        {
            ["PROMPTWARE_DIR"] = "/promptwares/UnknownPromptware"
        };
        var resolution = AgentProviderFactory.Resolve(runner, settings, "UnknownPromptware", jobContext: jobContext);

        Assert.Equal("claude", resolution.AgentId);
        Assert.Equal("sonnet", resolution.Model);
        Assert.Equal("high", resolution.Effort);
        // Base tools + _default's Write (unrestricted)
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Write", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_ExecutePlanGetsUnrestrictedBash()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["ExecutePlan"] = new()
                { Profile = "deep", AllowedTools = new List<string> { "Write(/extra/**)" } }
            },
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "claude",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "deep", Model = "opus", Effort = "max" }
                    }
                }
            });

        var jobContext = new Dictionary<string, string>
        {
            ["PROMPTWARE_DIR"] = "/promptwares/ExecutePlan"
        };
        var resolution = AgentProviderFactory.Resolve(runner, settings, "ExecutePlan", jobContext: jobContext);

        Assert.Equal("opus", resolution.Model);
        Assert.Equal("max", resolution.Effort);
        // Base tools + built-in ExecutePlan extras + config extras
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        Assert.Contains("Write(/extra/**)", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_ProfileOverrideTakesPrecedence()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["CreatePlan"] = new() { Profile = "balanced" }
            },
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "claude",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "balanced", Model = "sonnet", Effort = "high" },
                        new() { Name = "deep", Model = "opus", Effort = "max" }
                    }
                }
            });

        var resolution = AgentProviderFactory.Resolve(runner, settings, "CreatePlan", "deep");

        Assert.Equal("opus", resolution.Model);
        Assert.Equal("max", resolution.Effort);
    }

    [Fact]
    public void Resolve_IncludesBaseAgentArguments()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new() { Profile = "balanced" }
            },
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "claude",
                    Arguments = "--skip-confirm --verbose",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "balanced", Model = "sonnet", Arguments = "--max-turns 50" }
                    }
                }
            });

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");

        Assert.Contains("--skip-confirm", resolution.ExtraArgs);
        Assert.Contains("--verbose", resolution.ExtraArgs);
        Assert.Contains("--max-turns", resolution.ExtraArgs);
        Assert.Contains("50", resolution.ExtraArgs);
    }

    [Fact]
    public void Resolve_NoConfigStillReturnsBaseTools()
    {
        var runner = CreateRunner();
        var settings = CreateSettings();
        var jobContext = new Dictionary<string, string>
        {
            ["PROMPTWARE_DIR"] = "/promptwares/SomePromptware"
        };
        var resolution = AgentProviderFactory.Resolve(runner, settings, "SomePromptware", jobContext: jobContext);

        Assert.Equal("claude", resolution.AgentId);
        Assert.Equal("", resolution.Model);
        Assert.Equal("", resolution.Effort);
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Glob", resolution.AllowedTools);
        Assert.Contains("Grep", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        Assert.Contains("WebSearch", resolution.AllowedTools);
        Assert.Empty(resolution.ExtraArgs);
    }

    [Fact]
    public void Resolve_CodexProvider()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            "codex",
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "codex",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "default", Model = "o4-mini", Effort = "medium" }
                    }
                }
            },
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new() { Profile = "default" }
            });

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");

        Assert.Equal("codex", resolution.AgentId);
        Assert.Equal("o4-mini", resolution.Model);
        Assert.Equal("", resolution.Effort); // Codex CLI doesn't support EffortControl
    }

    [Fact]
    public void Resolve_MatchesCapitalizedAgentName_ClaudeCode()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "ClaudeCode",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "balanced", Model = "sonnet", Effort = "high" }
                    }
                }
            },
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new() { Profile = "balanced" }
            });

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");
        Assert.Equal("sonnet", resolution.Model);
    }

    [Fact]
    public void Resolve_MatchesCapitalizedAgentName_Codex()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            "codex",
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "Codex",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "default", Model = "o4-mini", Effort = "medium" }
                    }
                }
            },
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new() { Profile = "default" }
            });

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");
        Assert.Equal("o4-mini", resolution.Model);
    }

    [Fact]
    public void Resolve_MatchesCapitalizedAgentName_Antigravity()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            "antigravity",
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "Antigravity",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "default", Model = "antigravity-2.5-pro" }
                    }
                }
            },
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new() { Profile = "default" }
            });

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");
        Assert.Equal("", resolution.Model); // Antigravity CLI doesn't support ModelSelection
    }

    [Fact]
    public void Resolve_MatchesCapitalizedAgentName_Copilot()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            "copilot",
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "Copilot",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "default", Model = "gpt-5.2", Effort = "medium" }
                    }
                }
            },
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new() { Profile = "default" }
            });

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");
        Assert.Equal("gpt-5.2", resolution.Model);
    }

    [Fact]
    public void Resolve_ExpandsJobContextVariables()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["CreatePlan"] = new()
                {
                    Profile = "balanced",
                    AllowedTools = new List<string>
                    {
                        "Write(%PLANS_DIR%/**)", "Edit(%PLAN_DIR%/**)"
                    }
                }
            });

        var jobContext = new Dictionary<string, string>
        {
            ["PROMPTWARE_DIR"] = @"D:\Tendril\Promptwares\CreatePlan",
            ["PLANS_DIR"] = @"D:\Tendril\Plans",
            ["PLAN_DIR"] = @"D:\Tendril\Plans\01234-MyPlan"
        };

        var resolution = AgentProviderFactory.Resolve(runner, settings, "CreatePlan", jobContext: jobContext);

        // Base tools always present
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        // Config extras with expanded variables
        Assert.Contains("Write(D:/Tendril/Plans/**)", resolution.AllowedTools);
        Assert.Contains("Edit(D:/Tendril/Plans/01234-MyPlan/**)", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_JobContextExpansionIsCaseInsensitive()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["Test"] = new()
                {
                    AllowedTools = new List<string> { "Write(%plans_dir%/**)" }
                }
            });

        var jobContext = new Dictionary<string, string> { ["PLANS_DIR"] = "/home/plans" };

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test", jobContext: jobContext);

        Assert.Contains("Write(/home/plans/**)", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_ConfigToolsAddToBaseTools()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["Test"] = new()
                {
                    AllowedTools = new List<string> { "Write(/extra/**)" }
                }
            });

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");

        // Base tools present
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Glob", resolution.AllowedTools);
        Assert.Contains("Grep", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        Assert.Contains("WebSearch", resolution.AllowedTools);
        // Config extra
        Assert.Contains("Write(/extra/**)", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_ExecutePlanGetsBuiltInWriteEditAndBash()
    {
        var runner = CreateRunner();
        var settings = CreateSettings();

        var jobContext = new Dictionary<string, string>
        {
            ["PROMPTWARE_DIR"] = "/promptwares/ExecutePlan",
            ["PLAN_DIR"] = "/plans/01234-Test"
        };

        var resolution = AgentProviderFactory.Resolve(runner, settings, "ExecutePlan", jobContext: jobContext);

        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
        Assert.Contains("Write", resolution.AllowedTools);
        Assert.Contains("Edit", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_DeduplicatesTools()
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["Test"] = new()
                {
                    AllowedTools = new List<string> { "Read", "Bash" }
                }
            });

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");

        // Should not have duplicate Read or Bash
        Assert.Equal(resolution.AllowedTools.Count, resolution.AllowedTools.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("antigravity")]
    [InlineData("copilot")]
    [InlineData("opencode")]
    public void Resolve_UpdateProject_GetsBaseToolsForAllAgents(string agent)
    {
        var runner = CreateRunner();
        var settings = CreateSettings(
            agent,
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["UpdateProject"] = new() { Profile = "deep" }
            },
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = agent,
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "deep", Model = "test-model", Effort = "max" }
                    }
                }
            });

        var jobContext = new Dictionary<string, string>
        {
            ["PROMPTWARE_DIR"] = "/promptwares/UpdateProject"
        };

        var resolution = AgentProviderFactory.Resolve(runner, settings, "UpdateProject", jobContext: jobContext);

        Assert.Equal(agent, resolution.AgentId);
        var cli = runner.GetCli(agent);
        if (cli.Capabilities.HasFlag(AgentCapabilities.ModelSelection))
            Assert.Equal("test-model", resolution.Model);
        else
            Assert.Equal("", resolution.Model);
        if (cli.Capabilities.HasFlag(AgentCapabilities.EffortControl))
            Assert.Equal("max", resolution.Effort);
        else
            Assert.Equal("", resolution.Effort);

        // Base tools
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Glob", resolution.AllowedTools);
        Assert.Contains("Grep", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        Assert.Contains("WebSearch", resolution.AllowedTools);

        Assert.Equal(6, resolution.AllowedTools.Count);
    }
}
