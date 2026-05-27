using System.Reactive.Linq;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Test.End2End.Fixtures;

namespace Ivy.Tendril.Agents.Test.End2End.Tests;

[Collection("Agents")]
public class ClaudeEnd2EndTests(AgentFixture fixture)
{
    private const string Agent = AgentId.Claude;

    [SkippableFact]
    public async Task SimplePrompt_ReturnsSuccessfulResult()
    {
        Skip.If(!fixture.IsAvailable(Agent), fixture.SkipReasonIfUnavailable(Agent));

        var context = new AgentResolutionContext
        {
            AgentId = Agent,
            Prompt = "What is 2+2? Reply with just the number.",
            WorkingDirectory = fixture.WorkingDirectory,
            TimeoutPolicy = new TimeoutPolicy { TotalTimeout = TimeSpan.FromMinutes(2) },
        };

        await using var session = await fixture.Runner.LaunchAsync(context);
        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess, $"Exit code: {result.ExitCode}");
        Assert.NotNull(result.Response);
        Assert.Contains("4", result.Response);
    }

    [SkippableFact]
    public async Task SimplePrompt_EmitsTextEvent()
    {
        Skip.If(!fixture.IsAvailable(Agent), fixture.SkipReasonIfUnavailable(Agent));

        var context = new AgentResolutionContext
        {
            AgentId = Agent,
            Prompt = "Say exactly: hello world",
            WorkingDirectory = fixture.WorkingDirectory,
            TimeoutPolicy = new TimeoutPolicy { TotalTimeout = TimeSpan.FromMinutes(2) },
        };

        var textEvents = new List<TextEvent>();

        await using var session = await fixture.Runner.LaunchAsync(context);
        session.Events.OfType<TextEvent>().Subscribe(e => textEvents.Add(e));
        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(textEvents);
    }

    [SkippableFact]
    public async Task SimplePrompt_EmitsSessionInit()
    {
        Skip.If(!fixture.IsAvailable(Agent), fixture.SkipReasonIfUnavailable(Agent));

        var context = new AgentResolutionContext
        {
            AgentId = Agent,
            Prompt = "Reply with OK",
            WorkingDirectory = fixture.WorkingDirectory,
            TimeoutPolicy = new TimeoutPolicy { TotalTimeout = TimeSpan.FromMinutes(2) },
        };

        SessionInitEvent? initEvent = null;

        await using var session = await fixture.Runner.LaunchAsync(context);
        session.Events.OfType<SessionInitEvent>().Subscribe(e => initEvent = e);
        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(initEvent);
        Assert.NotNull(initEvent!.Model);
    }

    [SkippableFact]
    public async Task ToolUse_ReadFile_Works()
    {
        Skip.If(!fixture.IsAvailable(Agent), fixture.SkipReasonIfUnavailable(Agent));

        var testFile = Path.Combine(fixture.WorkingDirectory, "test.txt");
        await File.WriteAllTextAsync(testFile, "The secret number is 42.");

        var context = new AgentResolutionContext
        {
            AgentId = Agent,
            Prompt = $"Read the file test.txt and tell me what the secret number is. Reply with just the number.",
            WorkingDirectory = fixture.WorkingDirectory,
            TimeoutPolicy = new TimeoutPolicy { TotalTimeout = TimeSpan.FromMinutes(2) },
        };

        var toolCalls = new List<ToolCallEvent>();

        await using var session = await fixture.Runner.LaunchAsync(context);
        session.Events.OfType<ToolCallEvent>().Subscribe(e => toolCalls.Add(e));
        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Contains("42", result.Response);
        Assert.NotEmpty(toolCalls);
    }
}
