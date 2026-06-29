using System.Threading;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Onboarding.Helpers;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectAgentStepView(
    IState<List<RepoRef>> selectedRepos,
    IState<string> projectName,
    IState<bool> isStepLoading,
    OnboardingVerificationSession session,
    Action onBack,
    Action onNext,
    Action? onSkip = null,
    bool skipAgent = false,
    bool showHeader = true,
    IState<bool>? setupTrigger = null) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var setupService = UseService<IOnboardingSetupService>();
        var runner = UseService<IPromptwareRunner>();
        var agentRunner = UseService<IAgentRunner>();
        var client = UseService<IClientProvider>();

        var progressMessage = UseState<string?>(null);
        var progressValue = UseState<int?>(null);
        var error = UseState<string?>(null);
        var authCode = UseState<string?>(null);
        var isCloning = UseState(false);

        UseEffect(async () =>
        {
            if (session.Started.Value) return;

            // If setupTrigger is provided, wait for it to be true
            if (setupTrigger != null && !setupTrigger.Value) return;

            error.Set(null);
            isCloning.Set(true);
            isStepLoading.Set(true);

            var progressCts = new CancellationTokenSource();
            _ = UxHelper.AnimateProgressAsync(progressValue, progressCts.Token);

            try
            {
                var name = InputSanitizer.SanitizeProjectName(projectName.Value);
                var existingProject = config.Settings.Projects
                    .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (existingProject == null)
                {
                    var tendrilHome = config.TendrilHome;
                    if (string.IsNullOrEmpty(tendrilHome))
                    {
                        tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")
                                      ?? Path.Combine(
                                          Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                          ".tendril");
                    }

                    var refs = await OnboardingRepoHelper.ResolveReposAsync(
                        selectedRepos.Value, tendrilHome, progressMessage, error, isCloning, isStepLoading);

                    if (refs == null)
                    {
                        await progressCts.CancelAsync();
                        progressValue.Set(null);
                        return;
                    }

                    var project = new ProjectConfig
                    {
                        Name = name,
                        Color = "Green",
                        Repos = refs,
                        Context = "",
                        Verifications = [],
                        ReviewActions = []
                    };

                    config.SetPendingProject(project);
                    config.SetPendingVerificationDefinitions([]);
                }

                await progressCts.CancelAsync();
                progressValue.Set(100);
                progressMessage.Set("Running agent...");

                await setupService.CommitPendingProjectAsync();

                isCloning.Set(false);
                progressValue.Set(null);
                progressMessage.Set(null);

                if (skipAgent)
                {
                    isStepLoading.Set(false);
                    onNext();
                    return;
                }

                // Check active coding agent installation and authentication status before agentic run
                var agentKey = config.Settings.CodingAgent ?? "claude";
                var healthCheck = agentRunner.GetHealthCheck(agentKey);
                var info = healthCheck.GetOnboardingInfo();

                var agentCheckCts = new CancellationTokenSource();
                _ = UxHelper.AnimateProgressAsync(progressValue, agentCheckCts.Token);

                try
                {
                    progressMessage.Set($"Checking {info.DisplayName} installation...");
                    var installStatus = await healthCheck.CheckInstallAsync();
                    if (!installStatus.IsInstalled)
                    {
                        await agentCheckCts.CancelAsync();
                        progressValue.Set(null);
                        progressMessage.Set(null);
                        error.Set($"Please make sure your agent ({info.DisplayName}) is present and you are authorized.");
                        isStepLoading.Set(false);
                        return;
                    }

                    progressMessage.Set($"Verifying {info.DisplayName} authentication...");
                    var authStatus = await healthCheck.CheckAuthAsync();
                    if (authStatus.Status != AuthStatus.Authenticated)
                    {
                        progressMessage.Set($"Signing In to {info.DisplayName}... (Browser Will Open)");
                        authCode.Set(null);

                        var callbacks = new AuthFlowCallbacks
                        {
                            OnUrl = url => { client.OpenUrl(url); return Task.CompletedTask; },
                            OnCode = code => authCode.Set(code),
                        };
                        await healthCheck.RunAuthFlowAsync(callbacks, CancellationToken.None);
                        authCode.Set(null);

                        progressMessage.Set($"Verifying {info.DisplayName} authentication...");
                        authStatus = await healthCheck.CheckAuthAsync();
                        if (authStatus.Status != AuthStatus.Authenticated)
                        {
                            await agentCheckCts.CancelAsync();
                            progressValue.Set(null);
                            progressMessage.Set(null);
                            error.Set($"Please make sure your agent ({info.DisplayName}) is present and you are authorized.");
                            isStepLoading.Set(false);
                            return;
                        }
                    }
                }
                finally
                {
                    await agentCheckCts.CancelAsync();
                    progressValue.Set(null);
                    progressMessage.Set(null);
                }

                var notifyingStream = new NotifyingStream<string>(
                    session.Stream,
                    () => session.HasOutput.Set(true));

                var handle = runner.Run(new PromptwareRunOptions
                {
                    Promptware = "SetupProject",
                    Values = new Dictionary<string, string>
                    {
                        ["ProjectName"] = name,
                        ["Instructions"] = "Setup verifications and review actions"
                    }
                }, notifyingStream);

                session.Handle.Set(handle);
                session.Running.Set(true);
                session.Started.Set(true);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await handle.Completion;
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (!session.Cancelled.Value)
                            session.Error.Set($"Setup failed: {ex.Message}");
                    }
                    finally
                    {
                        if (!session.Cancelled.Value)
                        {
                            config.ReloadSettings();
                            session.RefreshToken.Set(session.RefreshToken.Value + 1);
                        }
                        session.Handle.Set(null);
                        session.Running.Set(false);
                        isStepLoading.Set(false);
                    }
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                await progressCts.CancelAsync();
                progressValue.Set(null);
                progressMessage.Set(null);
                error.Set($"Failed to set up project: {ex.Message}");
                isCloning.Set(false);
                isStepLoading.Set(false);
                session.Running.Set(false);
            }
        }, setupTrigger != null ? [setupTrigger, EffectTrigger.OnMount()] : [EffectTrigger.OnMount()]);

        var running = session.Running.Value || isCloning.Value;

        var buttonArea = Layout.Horizontal().Width(Size.Full())
            | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                .OnClick(onBack)
            | new Spacer()
            | (onSkip != null ? (object)new Button("Skip").Ghost().Large().OnClick(onSkip) : new Spacer())
            | new Button("Next").Secondary().Large().Icon(Icons.ArrowRight, Align.Right)
                .Disabled(running)
                .OnClick(onNext);

        // The agent output stream always renders while the agent is running. Before any
        // output arrives, the AgentViewer's own status label (below the stream) shows the
        // "Starting…" loading indicator, so we don't render a separate Loading() above it —
        // that avoided a layout shift when the bordered/padded Box swapped in on first output.
        var showStream = !isCloning.Value && (session.Running.Value || session.HasOutput.Value);

        var viewer = new AgentViewer()
            .Stream(session.Stream)
            .AutoScroll(true)
            .ShowStatusLabel(true)
            .Width(Size.Full())
            .Height(Size.Full()) with
        {
            OnComplete = _ =>
            {
                session.Running.Set(false);
                isStepLoading.Set(false);
                return ValueTask.CompletedTask;
            }
        };

        return Layout.Vertical().Margin(0, 0, 0, 2)
               | (showHeader ? Text.H3("Setting up your project") : null!)
               | Text.Muted(isCloning.Value
                   ? (progressMessage.Value ?? "Setting up your project...")
                   : "Tendril is detecting your tech stack and configuring your agentic harness.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | (session.Error.Value != null ? Text.Danger(session.Error.Value) : null!)
               | (authCode.Value != null
                   ? (object)Text.Markdown($"**Device code:** `{authCode.Value}` — enter this in your browser if prompted.")
                   : null!)
               | (isCloning.Value && progressValue.Value != null
                   ? (object)new Progress(progressValue.Value.Value)
                   : null!)
               | (showStream
                   ? (object)new Box(viewer)
                        .Width(Size.Full())
                        .Height(Size.Units(100).Max(Size.Fraction(0.6f)))
                        .Padding(4, 4, 0, 4)
                   : null!)
               | buttonArea
               | (showHeader ? (object)new Spacer().Height(Size.Units(4)) : null!);
    }
}
