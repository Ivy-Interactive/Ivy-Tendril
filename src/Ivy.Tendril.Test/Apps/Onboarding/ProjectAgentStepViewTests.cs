using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Apps.Onboarding;

public class ProjectAgentStepViewTests
{
    private static (OnboardingVerificationSession session, State<bool> isStepLoading, State<string?> error) CreateSessionContext()
    {
        var stream = new FakeWriteStream<string>();
        var handle = new State<PromptwareRunHandle?>(null);
        var hasOutput = new State<bool>(false);
        var running = new State<bool>(false);
        var started = new State<bool>(false);
        var cancelled = new State<bool>(false);
        var sessionError = new State<string?>(null);
        var refreshToken = new State<int>(0);

        var session = new OnboardingVerificationSession(
            stream,
            handle,
            hasOutput,
            running,
            started,
            cancelled,
            sessionError,
            refreshToken);

        var isStepLoading = new State<bool>(false);
        var error = new State<string?>(null);

        return (session, isStepLoading, error);
    }

    public static bool ComputeAboutToStart(
        IState<bool>? setupTrigger,
        IState<bool> sessionStarted,
        IState<bool> isStepLoading,
        IState<string?> error,
        IState<string?> sessionError)
    {
        return (setupTrigger == null || setupTrigger.Value) &&
               !sessionStarted.Value &&
               isStepLoading.Value &&
               error.Value == null &&
               sessionError.Value == null;
    }

    public static bool ComputeRunning(
        IState<bool> sessionRunning,
        IState<bool> isCloning,
        bool aboutToStart)
    {
        return sessionRunning.Value || isCloning.Value || aboutToStart;
    }

    public static bool ComputeShowStream(
        IState<bool> isCloning,
        IState<bool> sessionRunning,
        IState<bool> sessionHasOutput,
        bool aboutToStart)
    {
        return !isCloning.Value && (sessionRunning.Value || sessionHasOutput.Value || aboutToStart);
    }

    [Fact]
    public void InitialState_WhenLoading_AboutToStartIsTrue()
    {
        var (session, isStepLoading, error) = CreateSessionContext();
        isStepLoading.Set(true);

        var aboutToStart = ComputeAboutToStart(null, session.Started, isStepLoading, error, session.Error);
        var isCloning = new State<bool>(false);
        var running = ComputeRunning(session.Running, isCloning, aboutToStart);
        var showStream = ComputeShowStream(isCloning, session.Running, session.HasOutput, aboutToStart);

        Assert.True(aboutToStart);
        Assert.True(running);
        Assert.True(showStream);
    }

    [Fact]
    public void MissingAgent_WhenInstallCheckFailsOrCancelled_AboutToStartIsFalseAndRunningIsFalse()
    {
        var (session, isStepLoading, error) = CreateSessionContext();
        isStepLoading.Set(false);
        error.Set("Agent Claude Code is not installed. You can install it, go Back to choose another agent, or proceed with manual configuration.");

        var aboutToStart = ComputeAboutToStart(null, session.Started, isStepLoading, error, session.Error);
        var isCloning = new State<bool>(false);
        var running = ComputeRunning(session.Running, isCloning, aboutToStart);
        var showStream = ComputeShowStream(isCloning, session.Running, session.HasOutput, aboutToStart);

        Assert.False(aboutToStart);
        Assert.False(running);
        Assert.False(showStream);
    }

    [Fact]
    public void SetupException_WhenRunThrows_AboutToStartIsFalseAndRunningIsFalse()
    {
        var (session, isStepLoading, error) = CreateSessionContext();
        isStepLoading.Set(false);
        error.Set("Failed to set up project: agent binary not found");

        var aboutToStart = ComputeAboutToStart(null, session.Started, isStepLoading, error, session.Error);
        var isCloning = new State<bool>(false);
        var running = ComputeRunning(session.Running, isCloning, aboutToStart);
        var showStream = ComputeShowStream(isCloning, session.Running, session.HasOutput, aboutToStart);

        Assert.False(aboutToStart);
        Assert.False(running);
        Assert.False(showStream);
    }

    [Fact]
    public void SessionStartedAndRunning_AboutToStartIsFalse_RunningIsTrue()
    {
        var (session, isStepLoading, error) = CreateSessionContext();
        isStepLoading.Set(false);
        session.Started.Set(true);
        session.Running.Set(true);

        var aboutToStart = ComputeAboutToStart(null, session.Started, isStepLoading, error, session.Error);
        var isCloning = new State<bool>(false);
        var running = ComputeRunning(session.Running, isCloning, aboutToStart);
        var showStream = ComputeShowStream(isCloning, session.Running, session.HasOutput, aboutToStart);

        Assert.False(aboutToStart);
        Assert.True(running);
        Assert.True(showStream);
    }

    [Fact]
    public void SessionError_WhenBackgroundSetupFails_RunningIsFalseAndShowStreamIsFalse()
    {
        var (session, isStepLoading, error) = CreateSessionContext();
        isStepLoading.Set(false);
        session.Started.Set(true);
        session.Running.Set(false);
        session.Error.Set("Setup failed: process exited with code 1");

        var aboutToStart = ComputeAboutToStart(null, session.Started, isStepLoading, error, session.Error);
        var isCloning = new State<bool>(false);
        var running = ComputeRunning(session.Running, isCloning, aboutToStart);
        var showStream = ComputeShowStream(isCloning, session.Running, session.HasOutput, aboutToStart);

        Assert.False(aboutToStart);
        Assert.False(running);
        Assert.False(showStream);
    }

    [Fact]
    public void SkipAction_ResetsSessionStateAndAllowsNavigation()
    {
        var (session, isStepLoading, error) = CreateSessionContext();
        isStepLoading.Set(true);
        session.Started.Set(true);
        session.Running.Set(true);

        var navigatedStep = -1;
        Action onSkip = () =>
        {
            session.Reset();
            isStepLoading.Set(false);
            navigatedStep = 2;
        };

        onSkip();

        Assert.Equal(2, navigatedStep);
        Assert.False(session.Running.Value);
        Assert.False(session.Started.Value);
        Assert.False(isStepLoading.Value);
    }

    private sealed class FakeWriteStream<T> : IWriteStream<T>
    {
        public string Id => "fake-stream";
        public void Write(T data) { }
    }
}
