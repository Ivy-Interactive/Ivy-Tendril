using System.Reactive.Linq;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Test.End2End.Fixtures;

namespace Ivy.Tendril.Agents.Test.End2End.Tests;

[Collection("Agents")]
public class CodexEnd2EndTests(AgentFixture fixture)
{
    private const string Agent = AgentId.Codex;

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

        var textEvents = new List<TextEvent>();

        await using var session = await fixture.Runner.LaunchAsync(context);
        session.Events.OfType<TextEvent>().Subscribe(e => textEvents.Add(e));
        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess, $"Exit code: {result.ExitCode}");
        var allText = string.Join("", textEvents.Select(e => e.Text));
        Assert.Contains("4", allText);
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
    }
}
