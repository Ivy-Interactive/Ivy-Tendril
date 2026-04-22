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
                ["_default"] = new() { Profile = "balanced", AllowedTools = new List<string> { "Read", "Write" } }
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

        var resolution = AgentProviderFactory.Resolve(settings, "UnknownPromptware");

        Assert.IsType<ClaudeAgentProvider>(resolution.Provider);
        Assert.Equal("sonnet", resolution.Model);
        Assert.Equal("high", resolution.Effort);
        Assert.Equal(new[] { "Read", "Write" }, resolution.AllowedTools);
    }

    [Fact]
    public void Resolve_SpecificPromptwareOverridesDefault()
    {
        var settings = CreateSettings(
            promptwares: new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new() { Profile = "quick", AllowedTools = new List<string> { "Read" } },
                ["ExecutePlan"] = new()
                { Profile = "deep", AllowedTools = new List<string> { "Read", "Write", "Bash" } }
            },
            codingAgents: new List<AgentConfig>
            {
                new()
                {
                    Name = "claude",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "quick", Model = "haiku", Effort = "low" },
                        new() { Name = "deep", Model = "opus", Effort = "max" }
                    }
                }
            });

        var resolution = AgentProviderFactory.Resolve(settings, "ExecutePlan");

        Assert.Equal("opus", resolution.Model);
        Assert.Equal("max", resolution.Effort);
        Assert.Equal(new[] { "Read", "Write", "Bash" }, resolution.AllowedTools);
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
    public void Resolve_NoConfigReturnsEmptyModelAndEffort()
    {
        var settings = CreateSettings();
        var resolution = AgentProviderFactory.Resolve(settings, "SomePromptware");

        Assert.IsType<ClaudeAgentProvider>(resolution.Provider);
        Assert.Equal("", resolution.Model);
        Assert.Equal("", resolution.Effort);
        Assert.Empty(resolution.AllowedTools);
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
}