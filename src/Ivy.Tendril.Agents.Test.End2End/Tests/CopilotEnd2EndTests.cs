using System.Reactive.Linq;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Test.End2End.Fixtures;

namespace Ivy.Tendril.Agents.Test.End2End.Tests;

[Collection("Agents")]
public class CopilotEnd2EndTests(AgentFixture fixture)
{
    private const string Agent = AgentId.Copilot;

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
    public async Task SimplePrompt_EmitsEvents()
    {
        Skip.If(!fixture.IsAvailable(Agent), fixture.SkipReasonIfUnavailable(Agent));

        var context = new AgentResolutionContext
        {
            AgentId = Agent,
            Prompt = "Reply with OK",
            WorkingDirectory = fixture.WorkingDirectory,
            TimeoutPolicy = new TimeoutPolicy { TotalTimeout = TimeSpan.FromMinutes(2) },
        };

        var allEvents = new List<AgentEvent>();

        await using var session = await fixture.Runner.LaunchAsync(context);
        session.Events.Subscribe(e => allEvents.Add(e));
        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(allEvents);
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
            Prompt = "Read the file test.txt and tell me what the secret number is. Reply with just the number.",
            WorkingDirectory = fixture.WorkingDirectory,
            TimeoutPolicy = new TimeoutPolicy { TotalTimeout = TimeSpan.FromMinutes(2) },
        };

        var textEvents = new List<TextEvent>();

        await using var session = await fixture.Runner.LaunchAsync(context);
        session.Events.OfType<TextEvent>().Subscribe(e => textEvents.Add(e));
        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess);
        var allText = string.Join("", textEvents.Select(e => e.Text));
        Assert.Contains("42", allText);
    }
}
