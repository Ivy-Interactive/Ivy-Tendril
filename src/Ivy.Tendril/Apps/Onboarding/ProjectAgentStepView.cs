using Ivy.Tendril.Apps.Onboarding.Helpers;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Helpers;
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
    bool showHeader = true) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var setupService = UseService<IOnboardingSetupService>();
        var runner = UseService<IPromptwareRunner>();

        var progressMessage = UseState<string?>(null);
        var progressValue = UseState<int?>(null);
        var error = UseState<string?>(null);
        var isCloning = UseState(false);

        UseEffect(async () =>
        {
            if (session.Started.Value) return;

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

                var notifyingStream = new NotifyingStream<string>(
                    session.Stream,
                    () => session.HasOutput.Set(true));

                var handle = runner.Run(new PromptwareRunOptions
                {
                    Promptware = "UpdateProject",
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
                });
            }
            catch (Exception ex)
            {
                await progressCts.CancelAsync();
                progressValue.Set(null);
                progressMessage.Set(null);
                error.Set($"Failed to set up project: {ex.Message}");
                isCloning.Set(false);
                isStepLoading.Set(false);
            }
        }, [EffectTrigger.OnMount()]);

        var running = session.Running.Value || isCloning.Value;

        var buttonArea = Layout.Horizontal().Width(Size.Full())
            | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                .OnClick(onBack)
            | new Spacer()
            | (onSkip != null ? (object)new Button("Skip").Ghost().Large().OnClick(onSkip) : new Spacer())
            | new Button("Next").Secondary().Large().Icon(Icons.ArrowRight, Align.Right)
                .Disabled(running)
                .OnClick(onNext);

        var awaitingOutput = !isCloning.Value && session.Running.Value && !session.HasOutput.Value;

        return Layout.Vertical()
               | (showHeader ? Text.H3("Setting up your project") : null!)
               | Text.Muted(isCloning.Value
                   ? (progressMessage.Value ?? "Setting up your project...")
                   : "Tendril is detecting your tech stack and configuring your agentic harness.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | (session.Error.Value != null ? Text.Danger(session.Error.Value) : null!)
               | (isCloning.Value && progressValue.Value != null
                   ? (object)new Progress(progressValue.Value.Value)
                   : null!)
               | (awaitingOutput
                   ? (object)(Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                       | new Loading())
                   : null!)
               | (session.HasOutput.Value
                   ? (object)new Box(
                        new AgentViewer()
                            .Stream(session.Stream)
                            .AutoScroll(true)
                            .ShowStatusLabel(true)
                            .Width(Size.Full())
                            .Height(Size.Full())
                      )
                        .Width(Size.Full())
                        .Height(Size.Units(100).Max(Size.Fraction(0.6f)))
                        .Padding(4, 0, 2, 4)
                   : null!)
               | buttonArea
               | new Spacer().Height(Size.Units(4));
    }
}
