using System.Diagnostics;
using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Views;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectSetupStepView(
    IState<int> stepperIndex,
    IState<List<RepoRef>> selectedRepos,
    IState<string> projectName,
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
            var sanitized = SanitizeProjectName(raw);
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

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2("Setup your first project")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | new ProjectRepoPickerView(selectedRepos, projectName)
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back").Outline().Small().Icon(Icons.ArrowLeft)
                      .OnClick(() => stepperIndex.Set(stepperIndex.Value - 1))
                  | new Button("Skip Verifications").Ghost().Small()
                      .Disabled(!canContinue)
                      .OnClick(async () =>
                      {
                          var name = SanitizeProjectName(projectName.Value);
                          if (string.IsNullOrWhiteSpace(name)) return;

                          var existingProject = config.Settings.Projects
                              .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                          if (existingProject == null)
                          {
                              error.Set(null);
                              isCloning.Set(true);

                              var refs = await ResolveReposAsync(config, progressMessage, error, isCloning);
                              if (refs == null) return;

                              var project = new ProjectConfig
                              {
                                  Name = name,
                                  Color = "Green",
                                  Repos = refs,
                                  Context = "",
                                  Verifications = new List<ProjectVerificationRef>()
                              };

                              config.SetPendingProject(project);
                              config.SetPendingVerificationDefinitions(new List<VerificationConfig>());
                          }

                          await setupService.FinalizeOnboardingAsync();
                          await setupService.StartBackgroundServicesAsync();
                          clientProvider.ReloadPage();
                      })
                  | new Spacer()
                  | new Button("Generate Verifications").Primary().Large().Icon(Icons.Sparkles)
                      .Disabled(!canContinue)
                      .OnClick(async () =>
                      {
                          var name = SanitizeProjectName(projectName.Value);
                          if (string.IsNullOrWhiteSpace(name))
                          {
                              error.Set("Please enter a valid project name.");
                              return;
                          }

                          error.Set(null);
                          isCloning.Set(true);

                          var progressCts = new CancellationTokenSource();
                          _ = DriveProgressAsync(progressValue, progressCts.Token);

                          try
                          {
                              var refs = await ResolveReposAsync(config, progressMessage, error, isCloning);
                              if (refs == null)
                              {
                                  progressCts.Cancel();
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
                                  Verifications = new List<ProjectVerificationRef>()
                              };

                              config.SetPendingProject(project);
                              config.SetPendingVerificationDefinitions(new List<VerificationConfig>());

                              progressCts.Cancel();
                              progressValue.Set(100);
                              progressMessage.Set("Done");

                              await StartVerificationSessionAsync(config, setupService, runner, name);

                              await Task.Delay(250);

                              progressValue.Set(null);
                              progressMessage.Set(null);
                              isCloning.Set(false);
                              stepperIndex.Set(stepperIndex.Value + 1);
                          }
                          catch (Exception ex)
                          {
                              progressCts.Cancel();
                              progressValue.Set(null);
                              progressMessage.Set(null);
                              error.Set($"Failed to set up project: {ex.Message}");
                              isCloning.Set(false);
                          }
                      }));
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
            Values = new()
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
                session.Handle.Set((PromptwareRunHandle?)null);
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
                var success = await CloneRepositoryAsync(repo.Path, destPath);
                if (!success)
                {
                    error.Set($"Failed to fetch repository: {repo.Path}.");
                    isCloning.Set(false);
                    return null;
                }
                refs.Add(repo with { Path = destPath });
            }
        }

        return refs;
    }

    private static async Task DriveProgressAsync(IState<int?> value, CancellationToken ct)
    {
        value.Set(0);
        double current = 0;
        const double ceiling = 92.0;
        while (!ct.IsCancellationRequested)
        {
            var remaining = ceiling - current;
            var step = remaining * 0.06 + 0.4;
            current = Math.Min(ceiling - 0.5, current + step);
            value.Set((int)Math.Round(current));
            try { await Task.Delay(150, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static string SanitizeProjectName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Regex.Replace(input, @"[^A-Za-z0-9._-]", "");
    }

    private static async Task<bool> CloneRepositoryAsync(string url, string destinationPath)
    {
        try
        {
            var shell = "pwsh";

            if (url.Contains('\'') || url.Contains('"')) return false;

            string cmd;
            if (Directory.Exists(destinationPath))
            {
                cmd = $"git -C '{destinationPath}' pull";
            }
            else
            {
                cmd = $"git clone '{url}' '{destinationPath}'";
            }

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"-NoProfile -Command \"{cmd}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
