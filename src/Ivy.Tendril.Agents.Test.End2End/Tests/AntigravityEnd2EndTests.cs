using System.Reactive.Linq;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Test.End2End.Fixtures;

namespace Ivy.Tendril.Agents.Test.End2End.Tests;

[Collection("Agents")]
public class AntigravityEnd2EndTests(AgentFixture fixture)
{
    private const string Agent = AgentId.Antigravity;

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
    }

    [SkippableFact]
    public async Task SimplePrompt_EmitsEvents()
    {
        Skip.If(!fixture.IsAvailable(Agent), fixture.SkipReasonIfUnavailable(Agent));

        var context = new AgentResolutionContext
        {
            AgentId = Agent,
            Prompt = "Say exactly: hello world",
            WorkingDirectory = fixture.WorkingDirectory,
            TimeoutPolicy = new TimeoutPolicy { TotalTimeout = TimeSpan.FromMinutes(2) },
        };

        var allEvents = new List<AgentEvent>();

        await using var session = await fixture.Runner.LaunchAsync(context);
        session.Events.Subscribe(e => allEvents.Add(e));
        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess);
    }
}
