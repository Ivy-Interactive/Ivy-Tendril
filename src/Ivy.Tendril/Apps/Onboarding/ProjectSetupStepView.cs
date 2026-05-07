using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Views;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectSetupStepView(
    IState<int> stepperIndex,
    IState<string[]> ghOwners,
    IState<Dictionary<string, string[]>> ghReposByOwner,
    IState<List<RepoRef>> selectedRepos,
    IState<string> projectName) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var isCloning = UseState(false);
        var progressMessage = UseState<string?>(null);
        var progressValue = UseState<int?>(null);
        var error = UseState<string?>(null);

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
               | new ProjectRepoPickerView(selectedRepos, projectName,
                                           preFetchedOwners: ghOwners,
                                           preFetchedReposByOwner: ghReposByOwner)
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                      .OnClick(() => stepperIndex.Set(stepperIndex.Value - 1))
                  | new Spacer()
                  | new Button("Continue").Primary().Large().Icon(Icons.ArrowRight, Align.Right)
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
                                  if (!LooksLikeUrl(repo.Path))
                                  {
                                      progressMessage.Set($"Adding {repo.Path} ({i}/{total})...");
                                      var trimmed = repo.Path.Trim();
                                      if (!string.IsNullOrWhiteSpace(trimmed))
                                          refs.Add(repo with { Path = trimmed });
                                  }
                                  else
                                  {
                                      Directory.CreateDirectory(reposDir);
                                      var repoName = ExtractRepoName(repo.Path);
                                      progressMessage.Set($"Fetching {repoName} ({i}/{total})...");
                                      var destPath = Path.Combine(reposDir, repoName);
                                      var success = await GitHubCliHelper.CloneRepositoryAsync(repo.Path, destPath);
                                      if (!success)
                                      {
                                          progressCts.Cancel();
                                          progressValue.Set(null);
                                          progressMessage.Set(null);
                                          error.Set($"Failed to fetch repository: {repo.Path}.");
                                          isCloning.Set(false);
                                          return;
                                      }
                                      refs.Add(repo with { Path = destPath });
                                  }
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

    private static bool LooksLikeUrl(string path)
        => !string.IsNullOrEmpty(path)
           && (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("git@", StringComparison.OrdinalIgnoreCase));

    private static string ExtractRepoName(string url)
    {
        var trimmed = url;
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];
        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : Guid.NewGuid().ToString();
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
}
