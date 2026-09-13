using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class AgentFailureMessageTests
{
    [Fact]
    public void Describe_IdleTimeoutFiredWithFiveMinutes_MentionsMinutesAndIdleTimeout()
    {
        var message = AgentFailureMessage.Describe(
            idleTimeoutFired: true,
            idleTimeout: TimeSpan.FromMinutes(5),
            abortReason: null,
            exitCode: -1,
            state: SessionState.Failed,
            stderrTail: []);

        Assert.NotNull(message);
        Assert.Contains("5 minutes", message);
        Assert.Contains("idle timeout", message);
    }

    [Fact]
    public void Describe_IdleTimeoutFiredWithThirtySeconds_MentionsSeconds()
    {
        var message = AgentFailureMessage.Describe(
            idleTimeoutFired: true,
            idleTimeout: TimeSpan.FromSeconds(30),
            abortReason: null,
            exitCode: -1,
            state: SessionState.Failed,
            stderrTail: []);

        Assert.NotNull(message);
        Assert.Contains("30 seconds", message);
    }

    [Fact]
    public void Describe_AbortReasonSet_WinsOverNonZeroExitCode()
    {
        var message = AgentFailureMessage.Describe(
            idleTimeoutFired: false,
            idleTimeout: null,
            abortReason: "Agent execution timed out: total timeout limit of 15 minutes exceeded.",
            exitCode: 1,
            state: SessionState.Failed,
            stderrTail: ["some stderr"]);

        Assert.Equal("Agent execution timed out: total timeout limit of 15 minutes exceeded.", message);
    }

    [Fact]
    public void Describe_SessionStopped_ReportsStoppedNotCrashed()
    {
        var message = AgentFailureMessage.Describe(
            idleTimeoutFired: false,
            idleTimeout: null,
            abortReason: null,
            exitCode: -1,
            state: SessionState.Stopped,
            stderrTail: []);

        Assert.NotNull(message);
        Assert.Contains("stopped", message);
        Assert.DoesNotContain("crash", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_NonZeroExitWithStderrTail_IncludesLastLineAndExitCode()
    {
        var message = AgentFailureMessage.Describe(
            idleTimeoutFired: false,
            idleTimeout: null,
            abortReason: null,
            exitCode: 1,
            state: SessionState.Failed,
            stderrTail: ["first line", "last line"]);

        Assert.NotNull(message);
        Assert.Contains("1", message);
        Assert.Contains("last line", message);
    }

    [Fact]
    public void Describe_NonZeroExitWithNoStderr_ReportsExitCodeOnly()
    {
        var message = AgentFailureMessage.Describe(
            idleTimeoutFired: false,
            idleTimeout: null,
            abortReason: null,
            exitCode: 1,
            state: SessionState.Failed,
            stderrTail: []);

        Assert.NotNull(message);
        Assert.Contains("1", message);
        Assert.Contains("without reporting a result", message);
    }

    [Fact]
    public void Describe_StderrLineLongerThan500Characters_IsTruncated()
    {
        var longLine = new string('x', 600);

        var message = AgentFailureMessage.Describe(
            idleTimeoutFired: false,
            idleTimeout: null,
            abortReason: null,
            exitCode: 1,
            state: SessionState.Failed,
            stderrTail: [longLine]);

        Assert.NotNull(message);
        Assert.EndsWith("...", message);
        Assert.True(message.Length < 600 + 50);
    }

    [Fact]
    public void Describe_ExitCodeZeroWithNoFailureSignal_ReturnsNull()
    {
        var message = AgentFailureMessage.Describe(
            idleTimeoutFired: false,
            idleTimeout: null,
            abortReason: null,
            exitCode: 0,
            state: SessionState.Completed,
            stderrTail: []);

        Assert.Null(message);
    }
}
