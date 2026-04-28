using System.Diagnostics;
using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Test.Agents;

/// <summary>
/// Verifies that all agent providers satisfy the same baseline contract
/// so that any provider can be swapped in without breaking the job launcher.
/// </summary>
public class AgentProviderParityTests
{
    private static readonly IAgentProvider[] AllProviders =
    [
        new ClaudeAgentProvider(),
        new CodexAgentProvider(),
        new GeminiAgentProvider(),
        new CopilotAgentProvider()
    ];

    private static AgentInvocation CreateInvocation(
        string prompt = "Test prompt content",
        string workDir = "/tmp/work",
        string model = "test-model",
        string effort = "high",
        string sessionId = "sess-001",
        IReadOnlyList<string>? allowedTools = null,
        IReadOnlyList<string>? extraArgs = null) =>
        new(prompt, workDir, model, effort, sessionId,
            allowedTools ?? Array.Empty<string>(),
            extraArgs ?? Array.Empty<string>());

    public static IEnumerable<object[]> ProviderData =>
        AllProviders.Select(p => new object[] { p });

    // --- Process configuration ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_RedirectStdout(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.True(psi.RedirectStandardOutput, $"{provider.Name} must redirect stdout");
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_RedirectStderr(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.True(psi.RedirectStandardError, $"{provider.Name} must redirect stderr");
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_RedirectStdin(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.True(psi.RedirectStandardInput, $"{provider.Name} must redirect stdin");
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_DisableShellExecute(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.False(psi.UseShellExecute, $"{provider.Name} must not use shell execute");
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_CreateNoWindow(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.True(psi.CreateNoWindow, $"{provider.Name} must set CreateNoWindow");
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_SetUtf8Encoding(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.Equal(System.Text.Encoding.UTF8, psi.StandardOutputEncoding);
        Assert.Equal(System.Text.Encoding.UTF8, psi.StandardErrorEncoding);
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void StdinProviders_SetUtf8InputEncoding(IAgentProvider provider)
    {
        if (!provider.UsesStdinPrompt) return;
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.Equal(System.Text.Encoding.UTF8, psi.StandardInputEncoding);
    }

    // --- Working directory ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_SetWorkingDirectory(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation(workDir: "/custom/dir"));
        Assert.Equal("/custom/dir", psi.WorkingDirectory);
    }

    // --- Environment ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_SetCIEnvironment(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.True(psi.Environment.ContainsKey("CI"), $"{provider.Name} must set CI env var");
        Assert.Equal("true", psi.Environment["CI"]);
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_SetTermDumb(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.True(psi.Environment.ContainsKey("TERM"), $"{provider.Name} must set TERM env var");
        Assert.Equal("dumb", psi.Environment["TERM"]);
    }

    // --- Model ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_PassModel(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation(model: "my-model"));
        var args = psi.ArgumentList.ToList();
        var idx = args.IndexOf("--model");
        Assert.True(idx >= 0, $"{provider.Name} must pass --model");
        Assert.Equal("my-model", args[idx + 1]);
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_OmitModelWhenEmpty(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation(model: ""));
        Assert.DoesNotContain("--model", psi.ArgumentList.ToList());
    }

    // --- Extra args ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_IncludeExtraArgs(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation(extraArgs: new[] { "--custom", "value" }));
        var args = psi.ArgumentList.ToList();
        Assert.Contains("--custom", args);
        Assert.Contains("value", args);
    }

    // --- Prompt delivery ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_HavePromptDeliveryMechanism(IAgentProvider provider)
    {
        var promptText = "This is the prompt content for the agent";
        var psi = provider.BuildProcessStart(CreateInvocation(prompt: promptText));
        var args = psi.ArgumentList.ToList();

        if (provider.UsesStdinPrompt)
        {
            Assert.DoesNotContain(promptText, args);
        }
        else
        {
            Assert.Contains(promptText, args);
        }
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_LargePromptDoesNotExceedArgLimit(IAgentProvider provider)
    {
        var largePrompt = new string('x', 20_000);
        var psi = provider.BuildProcessStart(CreateInvocation(prompt: largePrompt));
        var args = psi.ArgumentList.ToList();

        if (!provider.UsesStdinPrompt)
        {
            // Providers that pass prompt via args should be aware of limits.
            // Claude and Codex use ArgumentList (not Arguments) so .NET handles
            // quoting, but the OS limit still applies. This test documents that.
            Assert.Contains(largePrompt, args);
        }
        else
        {
            Assert.DoesNotContain(largePrompt, args);
        }
    }

    // --- Non-empty name ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_HaveNonEmptyName(IAgentProvider provider)
    {
        Assert.False(string.IsNullOrWhiteSpace(provider.Name));
    }

    // --- FileName set ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_SetFileName(IAgentProvider provider)
    {
        var psi = provider.BuildProcessStart(CreateInvocation());
        Assert.False(string.IsNullOrWhiteSpace(psi.FileName), $"{provider.Name} must set FileName");
    }

    // --- ExtractResult handles empty input ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_ExtractResult_ReturnsNullForEmptyOutput(IAgentProvider provider)
    {
        var result = provider.ExtractResult(Array.Empty<string>());
        Assert.Null(result);
    }

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_ExtractResult_HandlesWhitespaceOnlyLines(IAgentProvider provider)
    {
        var lines = new List<string> { "", "  ", "\t" };
        var result = provider.ExtractResult(lines);
        Assert.Null(result);
    }

    // --- Writable directory passthrough ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_HandleWritableDirectoryTools(IAgentProvider provider)
    {
        var tools = new[] { "Read", "Write(/plans/**)", "Edit(/plans/01234/**)", "Bash" };
        var psi = provider.BuildProcessStart(CreateInvocation(allowedTools: tools));

        // Should not throw — all providers should gracefully handle writable tool specs
        Assert.NotNull(psi);
    }

    // --- Factory registration ---

    [Theory]
    [MemberData(nameof(ProviderData))]
    public void AllProviders_AreRegisteredInFactory(IAgentProvider provider)
    {
        var fromFactory = AgentProviderFactory.GetProvider(provider.Name);
        Assert.Equal(provider.GetType(), fromFactory.GetType());
    }
}
