using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Test.Agents;

public class AgentProviderFactoryTests
{
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
    [InlineData("claude", typeof(ClaudeAgentProvider))]
    [InlineData("codex", typeof(CodexAgentProvider))]
    [InlineData("gemini", typeof(GeminiAgentProvider))]
    [InlineData("copilot", typeof(CopilotAgentProvider))]
    public void GetProvider_ReturnsCorrectType(string name, Type expectedType)
    {
        var provider = AgentProviderFactory.GetProvider(name);
        Assert.IsType(expectedType, provider);
    }

    [Fact]
    public void GetProvider_CaseInsensitive()
    {
        var provider = AgentProviderFactory.GetProvider("Claude");
        Assert.IsType<ClaudeAgentProvider>(provider);
    }

    [Fact]
    public void GetProvider_ThrowsForUnknown()
    {
        Assert.Throws<ArgumentException>(() => AgentProviderFactory.GetProvider("unknown-agent"));
    }

    [Fact]
    public void Resolve_UsesDefaultProfile()
    {
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
        var resolution = AgentProviderFactory.Resolve(settings, "UnknownPromptware", jobContext: jobContext);

        Assert.IsType<ClaudeAgentProvider>(resolution.Provider);
        Assert.Equal("sonnet", resolution.Model);
        Assert.Equal("high", resolution.Effort);
        // Base tools + _default's Write (unrestricted)
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Write", resolution.AllowedTools);
        Assert.Contains("Bash(tendril*)", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_ExecutePlanGetsUnrestrictedBash()
    {
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
        var resolution = AgentProviderFactory.Resolve(settings, "ExecutePlan", jobContext: jobContext);

        Assert.Equal("opus", resolution.Model);
        Assert.Equal("max", resolution.Effort);
        // Base tools + built-in ExecutePlan extras + config extras
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
        Assert.Contains("Bash(tendril*)", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        Assert.Contains("Write(/extra/**)", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_ProfileOverrideTakesPrecedence()
    {
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

        var resolution = AgentProviderFactory.Resolve(settings, "CreatePlan", "deep");

        Assert.Equal("opus", resolution.Model);
        Assert.Equal("max", resolution.Effort);
    }

    [Fact]
    public void Resolve_IncludesBaseAgentArguments()
    {
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

        var resolution = AgentProviderFactory.Resolve(settings, "Test");

        Assert.Contains("--skip-confirm", resolution.ExtraArgs);
        Assert.Contains("--verbose", resolution.ExtraArgs);
        Assert.Contains("--max-turns", resolution.ExtraArgs);
        Assert.Contains("50", resolution.ExtraArgs);
    }

    [Fact]
    public void Resolve_NoConfigStillReturnsBaseTools()
    {
        var settings = CreateSettings();
        var jobContext = new Dictionary<string, string>
        {
            ["PROMPTWARE_DIR"] = "/promptwares/SomePromptware"
        };
        var resolution = AgentProviderFactory.Resolve(settings, "SomePromptware", jobContext: jobContext);

        Assert.IsType<ClaudeAgentProvider>(resolution.Provider);
        Assert.Equal("", resolution.Model);
        Assert.Equal("", resolution.Effort);
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Glob", resolution.AllowedTools);
        Assert.Contains("Grep", resolution.AllowedTools);
        Assert.Contains("Bash(tendril*)", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        Assert.Contains("WebSearch", resolution.AllowedTools);
        Assert.DoesNotContain("Bash", resolution.AllowedTools.Where(t => t == "Bash"));
        Assert.Empty(resolution.ExtraArgs);
    }

    [Fact]
    public void Resolve_CodexProvider()
    {
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

        var resolution = AgentProviderFactory.Resolve(settings, "Test");

        Assert.IsType<CodexAgentProvider>(resolution.Provider);
        Assert.Equal("o4-mini", resolution.Model);
        Assert.Equal("medium", resolution.Effort);
    }

    [Fact]
    public void Resolve_MatchesCapitalizedAgentName_ClaudeCode()
    {
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

        var resolution = AgentProviderFactory.Resolve(settings, "Test");
        Assert.Equal("sonnet", resolution.Model);
    }

    [Fact]
    public void Resolve_MatchesCapitalizedAgentName_Codex()
    {
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

        var resolution = AgentProviderFactory.Resolve(settings, "Test");
        Assert.Equal("o4-mini", resolution.Model);
    }

    [Fact]
    public void Resolve_MatchesCapitalizedAgentName_Gemini()
    {
        var settings = CreateSettings(
            "gemini",
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "Gemini",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "default", Model = "gemini-2.5-pro" }
                    }
                }
            },
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new() { Profile = "default" }
            });

        var resolution = AgentProviderFactory.Resolve(settings, "Test");
        Assert.Equal("gemini-2.5-pro", resolution.Model);
    }

    [Fact]
    public void Resolve_MatchesCapitalizedAgentName_Copilot()
    {
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

        var resolution = AgentProviderFactory.Resolve(settings, "Test");
        Assert.Equal("gpt-5.2", resolution.Model);
    }

    [Fact]
    public void Resolve_ExpandsJobContextVariables()
    {
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

        var resolution = AgentProviderFactory.Resolve(settings, "CreatePlan", jobContext: jobContext);

        // Base tools always present
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Bash(tendril*)", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        // Config extras with expanded variables
        Assert.Contains("Write(D:/Tendril/Plans/**)", resolution.AllowedTools);
        Assert.Contains("Edit(D:/Tendril/Plans/01234-MyPlan/**)", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_JobContextExpansionIsCaseInsensitive()
    {
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["Test"] = new()
                {
                    AllowedTools = new List<string> { "Write(%plans_dir%/**)" }
                }
            });

        var jobContext = new Dictionary<string, string> { ["PLANS_DIR"] = "/home/plans" };

        var resolution = AgentProviderFactory.Resolve(settings, "Test", jobContext: jobContext);

        Assert.Contains("Write(/home/plans/**)", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_ConfigToolsAddToBaseTools()
    {
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["Test"] = new()
                {
                    AllowedTools = new List<string> { "Write(/extra/**)" }
                }
            });

        var resolution = AgentProviderFactory.Resolve(settings, "Test");

        // Base tools present
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Glob", resolution.AllowedTools);
        Assert.Contains("Grep", resolution.AllowedTools);
        Assert.Contains("Bash(tendril*)", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        Assert.Contains("WebSearch", resolution.AllowedTools);
        // Config extra
        Assert.Contains("Write(/extra/**)", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_ExecutePlanGetsBuiltInWriteEditAndBash()
    {
        var settings = CreateSettings();

        var jobContext = new Dictionary<string, string>
        {
            ["PROMPTWARE_DIR"] = "/promptwares/ExecutePlan",
            ["PLAN_DIR"] = "/plans/01234-Test"
        };

        var resolution = AgentProviderFactory.Resolve(settings, "ExecutePlan", jobContext: jobContext);

        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
        Assert.Contains("Write", resolution.AllowedTools);
        Assert.Contains("Edit", resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_DeduplicatesTools()
    {
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["Test"] = new()
                {
                    AllowedTools = new List<string> { "Read", "Bash" }
                }
            });

        var resolution = AgentProviderFactory.Resolve(settings, "Test");

        // Should not have duplicate Read or Bash
        Assert.Equal(resolution.AllowedTools.Count, resolution.AllowedTools.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("claude", typeof(ClaudeAgentProvider))]
    [InlineData("codex", typeof(CodexAgentProvider))]
    [InlineData("gemini", typeof(GeminiAgentProvider))]
    [InlineData("copilot", typeof(CopilotAgentProvider))]
    [InlineData("opencode", typeof(OpenCodeAgentProvider))]
    public void Resolve_UpdateProject_GetsBaseToolsWithPromptwareDirForAllAgents(string agent, Type expectedProviderType)
    {
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

        var resolution = AgentProviderFactory.Resolve(settings, "UpdateProject", jobContext: jobContext);

        Assert.IsType(expectedProviderType, resolution.Provider);
        Assert.Equal("test-model", resolution.Model);
        Assert.Equal("max", resolution.Effort);

        // Base read-only tools
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Glob", resolution.AllowedTools);
        Assert.Contains("Grep", resolution.AllowedTools);
        Assert.Contains("Bash(tendril*)", resolution.AllowedTools);
        Assert.Contains("Bash(git *)", resolution.AllowedTools);
        Assert.Contains("Bash(gh *)", resolution.AllowedTools);
        Assert.Contains("Bash(ls *)", resolution.AllowedTools);
        Assert.Contains("Bash(find *)", resolution.AllowedTools);
        Assert.Contains("Bash(cat *)", resolution.AllowedTools);
        Assert.Contains("WebFetch", resolution.AllowedTools);
        Assert.Contains("WebSearch", resolution.AllowedTools);

        // Must NOT have unrestricted Bash
        Assert.DoesNotContain("Bash", resolution.AllowedTools.Where(t => t == "Bash"));

        Assert.Equal(11, resolution.AllowedTools.Count);
    }
}