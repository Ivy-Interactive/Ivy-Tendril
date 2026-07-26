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
        var jobService = UseService<IJobService>();

        var progressMessage = UseState<string?>(null);
        var progressValue = UseState<int?>(null);
        var error = UseState<string?>(null);
        var authCode = UseState<string?>(null);
        var isCloning = UseState(false);

        var (installDialog, showInstallDialog) = UseTrigger<InstallDialogArgs>((isOpen, args) =>
            new InstallMissingDialog(isOpen, args));

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
                var nameError = InputSanitizer.DescribeProjectNameError(projectName.Value);
                if (nameError != null)
                {
                    await progressCts.CancelAsync();
                    progressValue.Set(null);
                    progressMessage.Set(null);
                    error.Set(nameError);
                    isCloning.Set(false);
                    isStepLoading.Set(false);
                    return;
                }

                var name = projectName.Value;
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

                // Auto-initialize Promptwares vault in project repository folder(s)
                var proj = config.Settings.Projects
                    .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (proj != null)
                {
                    foreach (var repo in proj.Repos)
                    {
                        var repoPath = repo.Path;
                        if (!string.IsNullOrEmpty(repoPath) && Directory.Exists(repoPath))
                        {
                            PromptwareHelper.EnsureLocalVault(repoPath);
                        }
                    }
                }

                isCloning.Set(false);
                progressValue.Set(null);
                progressMessage.Set(null);

                if (proj != null && proj.Repos.Count > 0)
                {
                    progressMessage.Set("Starting setup job in background...");
                    var mainRepo = Environment.ExpandEnvironmentVariables(proj.Repos[0].Path);
                    jobService.StartJob(new SetupProjectArgs(mainRepo, name));
                }

                isStepLoading.Set(false);
                onNext();
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

        // With no setupTrigger the run starts on mount, so the step counts as
        // about-to-start from the moment it renders until the session has started.
        var aboutToStart = (setupTrigger == null || setupTrigger.Value) && !session.Started.Value;

        var running = session.Running.Value || isCloning.Value || aboutToStart;

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
        var showStream = !isCloning.Value && (session.Running.Value || session.HasOutput.Value || aboutToStart);

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
               | (showHeader ? (object)new Spacer().Height(Size.Units(4)) : null!)
               | installDialog;
    }
}
