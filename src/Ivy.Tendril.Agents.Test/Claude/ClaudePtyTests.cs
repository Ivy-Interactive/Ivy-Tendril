using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;

namespace Ivy.Tendril.Agents.Test.Claude;

public class ClaudePtyTests
{
    private readonly ClaudePty _pty = new();

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }

    [Fact]
    public void Id_IsClaude()
    {
        Assert.Equal(AgentId.Claude, _pty.Id);
    }

    [Fact]
    public void DisplayName_IsClaudeCode()
    {
        Assert.Equal("Claude Code", _pty.DisplayName);
    }

    [Fact]
    public void Capabilities_IncludesSessionResume()
    {
        Assert.True(_pty.Capabilities.HasFlag(AgentCapabilities.SessionResume));
    }

    [Fact]
    public void Capabilities_IncludesHealthCheck()
    {
        Assert.True(_pty.Capabilities.HasFlag(AgentCapabilities.HealthCheck));
    }

    [Fact]
    public void Capabilities_IncludesModelSelection()
    {
        Assert.True(_pty.Capabilities.HasFlag(AgentCapabilities.ModelSelection));
    }

    [Fact]
    public void Capabilities_IncludesEffortControl()
    {
        Assert.True(_pty.Capabilities.HasFlag(AgentCapabilities.EffortControl));
    }

    [Fact]
    public void SupportedTransports_IsPty()
    {
        Assert.Equal(TransportKind.Pty, _pty.SupportedTransports);
    }

    [Fact]
    public void TranslateToolName_KnownTool_ReturnsNative()
    {
        Assert.Equal("Read", _pty.TranslateToolName(CanonicalTools.Read));
        Assert.Equal("Write", _pty.TranslateToolName(CanonicalTools.Write));
        Assert.Equal("Edit", _pty.TranslateToolName(CanonicalTools.Edit));
        Assert.Equal("Bash", _pty.TranslateToolName(CanonicalTools.Bash));
        Assert.Equal("Glob", _pty.TranslateToolName(CanonicalTools.Glob));
        Assert.Equal("Grep", _pty.TranslateToolName(CanonicalTools.Grep));
        Assert.Equal("WebFetch", _pty.TranslateToolName(CanonicalTools.WebFetch));
        Assert.Equal("WebSearch", _pty.TranslateToolName(CanonicalTools.WebSearch));
    }

    [Fact]
    public void TranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_pty.TranslateToolName("unknown_tool"));
    }

    [Fact]
    public void ReverseTranslateToolName_KnownTool_ReturnsCanonical()
    {
        Assert.Equal(CanonicalTools.Read, _pty.ReverseTranslateToolName("Read"));
        Assert.Equal(CanonicalTools.Bash, _pty.ReverseTranslateToolName("Bash"));
    }

    [Fact]
    public void ReverseTranslateToolName_UnknownTool_ReturnsNull()
    {
        Assert.Null(_pty.ReverseTranslateToolName("UnknownNative"));
    }

    [Fact]
    public void ExtractWritableDirectories_ReturnsEmpty()
    {
        var dirs = _pty.ExtractWritableDirectories(["Read", "Write", "Bash"]);
        Assert.Empty(dirs);
    }

    [Fact]
    public void GetDefaultEnvironment_ContainsTerm()
    {
        var env = _pty.GetDefaultEnvironment();
        Assert.True(env.ContainsKey("TERM"));
        Assert.Equal("xterm-256color", env["TERM"]);
    }

    [Fact]
    public void GetActivityPatterns_ReturnsNonNull()
    {
        var patterns = _pty.GetActivityPatterns();
        Assert.NotNull(patterns);
    }

    [Fact]
    public void GetActivityPatterns_HasWorkingPattern()
    {
        var patterns = _pty.GetActivityPatterns()!;
        Assert.NotNull(patterns.WorkingPattern);
        Assert.Contains("⠋", patterns.WorkingPattern);
    }

    [Fact]
    public void GetActivityPatterns_HasIdlePattern()
    {
        var patterns = _pty.GetActivityPatterns()!;
        Assert.NotNull(patterns.IdlePattern);
        Assert.Contains("❯", patterns.IdlePattern);
    }

    [Fact]
    public void GetActivityPatterns_HasErrorPattern()
    {
        var patterns = _pty.GetActivityPatterns()!;
        Assert.NotNull(patterns.ErrorPattern);
    }

    [Fact]
    public void GetActivityPatterns_HasPermissionPromptPattern()
    {
        var patterns = _pty.GetActivityPatterns()!;
        Assert.NotNull(patterns.PermissionPromptPattern);
    }

    [Fact]
    public void BuildPtySpec_MinimalConfig_StartsWithClaude()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.Equal("claude", spec.CommandLine[0]);
        Assert.Equal("/tmp/test", spec.WorkingDirectory);
    }

    [Fact]
    public void BuildPtySpec_WithModel_IncludesModelFlag()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            Model = "claude-sonnet-4-5-20250514",
        };

        var spec = _pty.BuildPtySpec(config);

        var idx = IndexOf(spec.CommandLine,"--model");
        Assert.NotEqual(-1, idx);
        Assert.Equal("claude-sonnet-4-5-20250514", spec.CommandLine[idx + 1]);
    }

    [Fact]
    public void BuildPtySpec_FullAutoPermission_SkipsPermissions()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.FullAuto,
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.Contains("--dangerously-skip-permissions", spec.CommandLine);
        Assert.DoesNotContain("--permission-mode", spec.CommandLine);
    }

    [Fact]
    public void BuildPtySpec_AcceptEditsPermission_MapsCorrectly()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.AcceptEdits,
        };

        var spec = _pty.BuildPtySpec(config);

        var idx = IndexOf(spec.CommandLine,"--permission-mode");
        Assert.NotEqual(-1, idx);
        Assert.Equal("acceptEdits", spec.CommandLine[idx + 1]);
    }

    [Fact]
    public void BuildPtySpec_PlanPermission_MapsCorrectly()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.Plan,
        };

        var spec = _pty.BuildPtySpec(config);

        var idx = IndexOf(spec.CommandLine,"--permission-mode");
        Assert.NotEqual(-1, idx);
        Assert.Equal("plan", spec.CommandLine[idx + 1]);
    }

    [Fact]
    public void BuildPtySpec_DefaultPermission_MapsToDefault()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.Default,
        };

        var spec = _pty.BuildPtySpec(config);

        var idx = IndexOf(spec.CommandLine,"--permission-mode");
        Assert.NotEqual(-1, idx);
        Assert.Equal("default", spec.CommandLine[idx + 1]);
    }

    [Fact]
    public void BuildPtySpec_WithSessionId_IncludesFlag()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            SessionId = "abc-123",
        };

        var spec = _pty.BuildPtySpec(config);

        var idx = IndexOf(spec.CommandLine,"--session-id");
        Assert.NotEqual(-1, idx);
        Assert.Equal("abc-123", spec.CommandLine[idx + 1]);
    }

    [Fact]
    public void BuildPtySpec_WithSystemPrompt_WritesFileAndIncludesFlag()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            SystemPrompt = "Be helpful",
        };

        var spec = _pty.BuildPtySpec(config);

        var idx = IndexOf(spec.CommandLine, "--system-prompt-file");
        Assert.NotEqual(-1, idx);
        var filePath = spec.CommandLine[idx + 1];
        Assert.True(File.Exists(filePath));
        Assert.Equal("Be helpful", File.ReadAllText(filePath));
        File.Delete(filePath);
    }

    [Fact]
    public void BuildPtySpec_WithAppendSystemPrompt_UsesAppendFileFlag()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            SystemPrompt = "Extra instructions",
            AppendSystemPrompt = true,
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.Contains("--append-system-prompt-file", spec.CommandLine);
        Assert.DoesNotContain("--system-prompt-file", spec.CommandLine);
        var idx = IndexOf(spec.CommandLine, "--append-system-prompt-file");
        var filePath = spec.CommandLine[idx + 1];
        Assert.True(File.Exists(filePath));
        Assert.Equal("Extra instructions", File.ReadAllText(filePath));
        File.Delete(filePath);
    }

    [Fact]
    public void BuildPtySpec_WithMcpServers_IncludesFlags()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            McpServers = [new McpServerConfig("server1", "cmd1", []), new McpServerConfig("server2", "cmd2", [])],
        };

        var spec = _pty.BuildPtySpec(config);

        var mcpIndices = spec.CommandLine
            .Select((v, i) => (v, i))
            .Where(x => x.v == "--mcp-server")
            .Select(x => x.i)
            .ToList();

        Assert.Equal(2, mcpIndices.Count);
        Assert.Equal("server1", spec.CommandLine[mcpIndices[0] + 1]);
        Assert.Equal("server2", spec.CommandLine[mcpIndices[1] + 1]);
    }

    [Fact]
    public void BuildPtySpec_WithExtraArguments_AppendsToEnd()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            ExtraArguments = ["--verbose", "--debug"],
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.Contains("--verbose", spec.CommandLine);
        Assert.Contains("--debug", spec.CommandLine);
    }

    [Fact]
    public void BuildPtySpec_WithEnvironmentVariables_MergesWithDefaults()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["CUSTOM_VAR"] = "custom_value",
            },
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.Equal("xterm-256color", spec.Environment["TERM"]);
        Assert.Equal("custom_value", spec.Environment["CUSTOM_VAR"]);
    }

    [Fact]
    public void BuildPtySpec_EnvironmentOverride_CustomOverridesDefault()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["TERM"] = "dumb",
            },
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.Equal("dumb", spec.Environment["TERM"]);
    }

    [Fact]
    public void BuildPtySpec_NoModel_OmitsModelFlag()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.DoesNotContain("--model", spec.CommandLine);
    }

    [Fact]
    public void BuildPtySpec_NoSessionId_OmitsSessionIdFlag()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.DoesNotContain("--session-id", spec.CommandLine);
    }
}
