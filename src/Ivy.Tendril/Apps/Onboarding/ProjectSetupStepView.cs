using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Apps.Setup.Dialogs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Views;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectSetupStepView(
    IState<int> stepperIndex,
    IState<List<RepoRef>> selectedRepos,
    IState<string> projectName,
    IState<bool> isStepLoading,
    OnboardingVerificationSession session) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var setupService = UseService<IOnboardingSetupService>();
        var runner = UseService<IPromptwareRunner>();
        var clientProvider = UseService<IClientProvider>();
        var isCloning = UseState(false);
        var progressMessage = UseState<string?>(null);
        var progressValue = UseState<int?>(null);
        var error = UseState<string?>(null);
        var reviewActions = UseState(new List<ReviewActionConfig>());
        var (reviewActionTriggerView, showReviewActionTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new EditReviewActionDialogContent(isOpen, existingIndex, reviewActions));
        var (reviewActionAlertView, showReviewActionAlert) = UseAlert();

        UseEffect(() =>
        {
            if (selectedRepos.Value.Count == 0 && config.Settings.Projects.Count > 0)
            {
                var lastProject = config.Settings.Projects.Last();
                projectName.Set(lastProject.Name);
                selectedRepos.Set(lastProject.Repos.ToList());
            }
        }, [EffectTrigger.OnMount()]);

        UseEffect(() =>
        {
            var raw = projectName.Value ?? "";
            var sanitized = InputSanitizer.SanitizeProjectName(raw);
            if (sanitized != raw) projectName.Set(sanitized);
        }, projectName);

        if (isCloning.Value)
        {
            return Layout.Vertical().Margin(0, 0, 0, 20).Gap(4)
                   | Text.Block(progressMessage.Value ?? "Setting up your project...")
                   | (progressValue.Value != null
                       ? new Progress(progressValue.Value.Value)
                       : null!)
                   | (error.Value != null ? Text.Danger(error.Value) : null!);
        }

        var canContinue = selectedRepos.Value.Count > 0
                          && !string.IsNullOrWhiteSpace(projectName.Value);

        var buttonArea = Layout.Horizontal().Width(Size.Full())
            | new Button("Back").Outline().Icon(Icons.ArrowLeft)
                .OnClick(() => stepperIndex.Set(stepperIndex.Value - 1))
            | new Spacer()
            | new Button("Finish").Secondary()
                .OnClick(() => _ = FinishAsync(config, setupService, clientProvider, reviewActions, error, isCloning, progressMessage));

        var generateBlock = canContinue
            ? (object)(Layout.Vertical().Gap(2)
                | Text.Bold("Generate Verifications")
                | Text.Muted("Automatically detect your project's tech stack and configure verification steps (build, test, lint, etc.) that run after each plan execution.")
                | new Button("Generate Verifications").Primary().Large().Icon(Icons.Sparkles)
                    .OnClick(() => _ = GenerateVerificationsAsync(config, setupService, runner, reviewActions, error, isCloning, progressMessage, progressValue)))
            : null!;

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2("Setup your first project")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | new ProjectRepoPickerView(selectedRepos, projectName)
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | (Layout.Vertical().Gap(2)
                  | Text.Block("Review Actions").Bold()
                  | Text.Block("Commands that run during plan review (e.g., tests, linting).").Muted().Small()
                  | new ReviewActionsTableView(reviewActions, showReviewActionTrigger, showReviewActionAlert)
                  | new Button("Add Review Action").Icon(Icons.Plus).Outline()
                      .OnClick(() => showReviewActionTrigger(null)))
               | reviewActionTriggerView
               | reviewActionAlertView
               | (canContinue ? new Separator() : null!)
               | generateBlock
               | (canContinue ? new Separator() : null!)
               | buttonArea;
    }

    private async Task FinishAsync(
        IConfigService config,
        IOnboardingSetupService setupService,
        IClientProvider clientProvider,
        IState<List<ReviewActionConfig>> reviewActions,
        IState<string?> error,
        IState<bool> isCloning,
        IState<string?> progressMessage)
    {
        var name = InputSanitizer.SanitizeProjectName(projectName.Value);
        if (string.IsNullOrWhiteSpace(name))
        {
            await setupService.FinalizeOnboardingAsync();
            await setupService.StartBackgroundServicesAsync();
            clientProvider.ReloadPage();
            return;
        }

        var existingProject = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existingProject == null)
        {
            error.Set(null);
            isCloning.Set(true);
            isStepLoading.Set(true);

            var refs = await ResolveReposAsync(config, progressMessage, error, isCloning);
            if (refs == null) return;

            var project = new ProjectConfig
            {
                Name = name,
                Color = "Green",
                Repos = refs,
                Context = "",
                Verifications = [],
                ReviewActions = new List<ReviewActionConfig>(reviewActions.Value)
            };

            config.SetPendingProject(project);
            config.SetPendingVerificationDefinitions([]);
        }

        await setupService.FinalizeOnboardingAsync();
        await setupService.StartBackgroundServicesAsync();
        clientProvider.ReloadPage();
    }

    private async Task GenerateVerificationsAsync(
        IConfigService config,
        IOnboardingSetupService setupService,
        IPromptwareRunner runner,
        IState<List<ReviewActionConfig>> reviewActions,
        IState<string?> error,
        IState<bool> isCloning,
        IState<string?> progressMessage,
        IState<int?> progressValue)
    {
        var name = InputSanitizer.SanitizeProjectName(projectName.Value);
        if (string.IsNullOrWhiteSpace(name))
        {
            error.Set("Please enter a valid project name.");
            return;
        }

        error.Set(null);
        isCloning.Set(true);
        isStepLoading.Set(true);

        var progressCts = new CancellationTokenSource();
        _ = UxHelper.AnimateProgressAsync(progressValue, progressCts.Token);

        try
        {
            var refs = await ResolveReposAsync(config, progressMessage, error, isCloning);
            if (refs == null)
            {
                await progressCts.CancelAsync();
                progressValue.Set(null);
                progressMessage.Set(null);
                return;
            }

            var project = new ProjectConfig
            {
                Name = name,
                Color = "Green",
                Repos = refs,
                Context = "",
                Verifications = [],
                ReviewActions = new List<ReviewActionConfig>(reviewActions.Value)
            };

            config.SetPendingProject(project);
            config.SetPendingVerificationDefinitions([]);

            await progressCts.CancelAsync();
            progressValue.Set(100);
            progressMessage.Set("Done");

            await StartVerificationSessionAsync(config, setupService, runner, name);

            await Task.Delay(250, progressCts.Token);

            progressValue.Set(null);
            progressMessage.Set(null);
            isCloning.Set(false);
            isStepLoading.Set(false);
            stepperIndex.Set(stepperIndex.Value + 1);
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
    }

    private async Task StartVerificationSessionAsync(
        IConfigService config,
        IOnboardingSetupService setupService,
        IPromptwareRunner runner,
        string projectName)
    {
        session.Reset();
        await setupService.CommitPendingProjectAsync();

        var notifyingStream = new NotifyingStream<string>(
            session.Stream,
            () => session.HasOutput.Set(true));

        var handle = runner.Run(new PromptwareRunOptions
        {
            Promptware = "UpdateProject",
            Values = new Dictionary<string, string>
            {
                ["ProjectName"] = projectName,
                ["Instructions"] = "Setup verifications"
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
                    session.Error.Set($"Verification setup failed: {ex.Message}");
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
            }
        });
    }

    private async Task<List<RepoRef>?> ResolveReposAsync(
        IConfigService config,
        IState<string?> progressMessage,
        IState<string?> error,
        IState<bool> isCloning)
    {
        var refs = new List<RepoRef>();
        var tendrilHome = config.TendrilHome;
        if (string.IsNullOrEmpty(tendrilHome))
        {
            tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")
                          ?? Path.Combine(
                              Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                              ".tendril");
        }
        var reposDir = Path.Combine(tendrilHome, "Repos");

        var total = selectedRepos.Value.Count;
        var i = 0;
        foreach (var repo in selectedRepos.Value)
        {
            i++;
            var kind = RepoPathValidator.Classify(repo.Path);
            if (kind == RepoPathKind.LocalPath)
            {
                progressMessage.Set($"Adding {repo.Path} ({i}/{total})...");
                var trimmed = repo.Path.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    refs.Add(repo with { Path = trimmed });
            }
            else
            {
                Directory.CreateDirectory(reposDir);
                var repoName = RepoPathValidator.ExtractRepoName(repo.Path) ?? Guid.NewGuid().ToString();
                progressMessage.Set($"Fetching {repoName} ({i}/{total})...");
                var destPath = Path.Combine(reposDir, repoName);
                var success = await ProcessCheckHelper.CloneRepositoryAsync(repo.Path, destPath);
                if (!success)
                {
                    error.Set($"Failed to fetch repository: {repo.Path}.");
                    isCloning.Set(false);
                    isStepLoading.Set(false);
                    return null;
                }
                refs.Add(repo with { Path = destPath });
            }
        }

        return refs;
    }

}
