using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Test.Agents;

public class AgentProviderTests
{
    private static AgentInvocation CreateInvocation(
        string prompt = "test prompt",
        string workDir = "/tmp/work",
        string model = "sonnet",
        string effort = "high",
        string sessionId = "sess-123",
        IReadOnlyList<string>? allowedTools = null,
        IReadOnlyList<string>? extraArgs = null)
    {
        return new AgentInvocation(prompt, workDir, model, effort, sessionId,
            allowedTools ?? Array.Empty<string>(),
            extraArgs ?? Array.Empty<string>());
    }

    // --- Claude Provider ---

    [Fact]
    public void Claude_BuildProcessStart_SetsExecutable()
    {
        var provider = new ClaudeAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation());

        Assert.Equal("claude", psi.FileName);
    }

    [Fact]
    public void Claude_BuildProcessStart_IncludesBaseFlags()
    {
        var provider = new ClaudeAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation());

        var args = psi.ArgumentList.ToList();
        Assert.Contains("--print", args);
        Assert.Contains("--verbose", args);
        Assert.Contains("stream-json", args);
        Assert.Contains("--dangerously-skip-permissions", args);
    }

    [Fact]
    public void Claude_BuildProcessStart_IncludesModelAndEffort()
    {
        var provider = new ClaudeAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(model: "opus", effort: "max"));

        var args = psi.ArgumentList.ToList();
        var modelIdx = args.IndexOf("--model");
        Assert.True(modelIdx >= 0);
        Assert.Equal("opus", args[modelIdx + 1]);

        var effortIdx = args.IndexOf("--effort");
        Assert.True(effortIdx >= 0);
        Assert.Equal("max", args[effortIdx + 1]);
    }

    [Fact]
    public void Claude_BuildProcessStart_IncludesSessionId()
    {
        var provider = new ClaudeAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(sessionId: "abc-123"));

        var args = psi.ArgumentList.ToList();
        var idx = args.IndexOf("--session-id");
        Assert.True(idx >= 0);
        Assert.Equal("abc-123", args[idx + 1]);
    }

    [Fact]
    public void Claude_BuildProcessStart_IncludesAllowedTools()
    {
        var provider = new ClaudeAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(
            allowedTools: new[] { "Read", "Write", "Bash" }));

        var args = psi.ArgumentList.ToList();
        var idx = args.IndexOf("--allowedTools");
        Assert.True(idx >= 0);
        Assert.Equal("Read,Write,Bash", args[idx + 1]);
    }

    [Fact]
    public void Claude_BuildProcessStart_PromptAfterDoubleDash()
    {
        var provider = new ClaudeAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation("hello world"));

        var args = psi.ArgumentList.ToList();
        var dashIdx = args.IndexOf("--");
        Assert.True(dashIdx >= 0);
        Assert.Equal("hello world", args[dashIdx + 1]);
    }

    [Fact]
    public void Claude_BuildProcessStart_SetsEnvironment()
    {
        var provider = new ClaudeAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation());

        Assert.Equal("true", psi.Environment["CI"]);
        Assert.Equal("dumb", psi.Environment["TERM"]);
    }

    [Fact]
    public void Claude_BuildProcessStart_OmitsEmptyModel()
    {
        var provider = new ClaudeAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(model: ""));

        var args = psi.ArgumentList.ToList();
        Assert.DoesNotContain("--model", args);
    }

    [Fact]
    public void Claude_BuildProcessStart_IncludesExtraArgs()
    {
        var provider = new ClaudeAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(
            extraArgs: new[] { "--max-tokens", "4096" }));

        var args = psi.ArgumentList.ToList();
        Assert.Contains("--max-tokens", args);
        Assert.Contains("4096", args);
    }

    [Fact]
    public void Claude_ExtractResult_ParsesStreamJson()
    {
        var provider = new ClaudeAgentProvider();
        var lines = new List<string>
        {
            "{\"type\":\"status\",\"message\":\"working\"}",
            "{\"type\":\"result\",\"result\":\"Plan created successfully\"}"
        };

        var result = provider.ExtractResult(lines);
        Assert.Equal("Plan created successfully", result);
    }

    [Fact]
    public void Claude_ExtractResult_ReturnsNullForNoResult()
    {
        var provider = new ClaudeAgentProvider();
        var lines = new List<string>
        {
            "{\"type\":\"status\",\"message\":\"working\"}"
        };

        Assert.Null(provider.ExtractResult(lines));
    }

    // --- Codex Provider ---

    [Fact]
    public void Codex_BuildProcessStart_SetsExecutable()
    {
        var provider = new CodexAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation());

        Assert.Equal("codex", psi.FileName);
    }

    [Fact]
    public void Codex_BuildProcessStart_UsesExecSubcommand()
    {
        var provider = new CodexAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation());

        var args = psi.ArgumentList.ToList();
        Assert.Equal("exec", args[0]);
        Assert.Contains("--full-auto", args);
        Assert.Contains("--json", args);
    }

    [Fact]
    public void Codex_BuildProcessStart_DoesNotIncludeEffort()
    {
        var provider = new CodexAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(effort: "medium"));

        var args = psi.ArgumentList.ToList();
        Assert.DoesNotContain("--reasoning-effort", args);
        Assert.DoesNotContain("--effort", args);
    }

    [Fact]
    public void Codex_BuildProcessStart_UsesStdinForPrompt()
    {
        var provider = new CodexAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation("do the thing"));

        Assert.True(provider.UsesStdinPrompt);
        var args = psi.ArgumentList.ToList();
        Assert.Equal("-", args[^1]);
        Assert.DoesNotContain("do the thing", args);
    }

    // --- Gemini Provider ---

    [Fact]
    public void Gemini_BuildProcessStart_SetsExecutable()
    {
        var provider = new GeminiAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation());

        Assert.Equal("gemini", psi.FileName);
    }

    [Fact]
    public void Gemini_BuildProcessStart_UsesStdinForPrompt()
    {
        var provider = new GeminiAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation("my long prompt"));

        Assert.True(provider.UsesStdinPrompt);
        var args = psi.ArgumentList.ToList();
        Assert.Contains("--prompt", args);
        Assert.DoesNotContain("my long prompt", args);
    }

    [Fact]
    public void Gemini_BuildProcessStart_DoesNotIncludeEffort()
    {
        var provider = new GeminiAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(effort: "high"));

        // Gemini CLI does not support effort flag
        Assert.DoesNotContain("--effort", psi.ArgumentList.ToList());
        Assert.DoesNotContain("--reasoning-effort", psi.ArgumentList.ToList());
    }

    // --- ExtractWritableDirs ---

    [Fact]
    public void ExtractWritableDirs_ParsesWriteAndEditPatterns()
    {
        var tools = new[] { "Read", "Bash", "Write(/plans/**)", "Edit(/plans/01234/**)" };
        var dirs = CodexAgentProvider.ExtractWritableDirs(tools).ToList();

        Assert.Equal(2, dirs.Count);
        Assert.Equal("/plans", dirs[0]);
        Assert.Equal("/plans/01234", dirs[1]);
    }

    [Fact]
    public void ExtractWritableDirs_DeduplicatesSamePath()
    {
        var tools = new[] { "Write(/plans/**)", "Edit(/plans/**)" };
        var dirs = CodexAgentProvider.ExtractWritableDirs(tools).ToList();

        Assert.Single(dirs);
        Assert.Equal("/plans", dirs[0]);
    }

    [Fact]
    public void ExtractWritableDirs_IgnoresUnscopedTools()
    {
        var tools = new[] { "Read", "Write", "Edit", "Bash", "Glob", "Grep" };
        var dirs = CodexAgentProvider.ExtractWritableDirs(tools).ToList();

        Assert.Empty(dirs);
    }

    [Fact]
    public void ExtractWritableDirs_HandlesSingleStar()
    {
        var tools = new[] { "Write(/inbox/*)" };
        var dirs = CodexAgentProvider.ExtractWritableDirs(tools).ToList();

        Assert.Single(dirs);
        Assert.Equal("/inbox", dirs[0]);
    }

    // --- Codex writable dirs ---

    [Fact]
    public void Codex_BuildProcessStart_PassesAddDirForWritableTools()
    {
        var provider = new CodexAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(
            allowedTools: new[] { "Read", "Write(/plans/**)", "Bash" }));

        var args = psi.ArgumentList.ToList();
        var idx = args.IndexOf("--add-dir");
        Assert.True(idx >= 0);
        Assert.Equal("/plans", args[idx + 1]);
    }

    [Fact]
    public void Codex_BuildProcessStart_NoAddDirForUnscopedWrite()
    {
        var provider = new CodexAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(
            allowedTools: new[] { "Read", "Write", "Bash" }));

        var args = psi.ArgumentList.ToList();
        Assert.DoesNotContain("--add-dir", args);
    }

    // --- Gemini writable dirs ---

    [Fact]
    public void Gemini_BuildProcessStart_PassesIncludeDirectories()
    {
        var provider = new GeminiAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation(
            allowedTools: new[] { "Read", "Edit(/plans/01234/**)", "Bash" }));

        var args = psi.ArgumentList.ToList();
        var idx = args.IndexOf("--include-directories");
        Assert.True(idx >= 0);
        Assert.Equal("/plans/01234", args[idx + 1]);
    }

    [Fact]
    public void Gemini_BuildProcessStart_IncludesYoloFlag()
    {
        var provider = new GeminiAgentProvider();
        var psi = provider.BuildProcessStart(CreateInvocation());

        Assert.Contains("--yolo", psi.ArgumentList.ToList());
    }
}