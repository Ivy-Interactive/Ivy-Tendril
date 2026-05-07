using System.Text.RegularExpressions;
using Ivy.Core.Hooks;
using Ivy.Desktop;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public record RepoChoice(string Owner, string Name, bool IsLocal = false, string? LocalPath = null)
{
    public string DisplayLabel => IsLocal ? (LocalPath ?? Name) : $"{Owner}/{Name}";
}

public class ProjectSetupStepView(
    IState<int> stepperIndex,
    IState<string[]> ghOwners,
    IState<Dictionary<string, string[]>> ghReposByOwner,
    IState<string> selectedOwner,
    IState<List<RepoChoice>> selectedRepos,
    IState<string> projectName) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var mode = UseState("remote"); // "remote" | "local"
        var selectedRepo = UseState("");
        var newLocalPath = UseState("");
        var isCloning = UseState(false);
        var progressMessage = UseState<string?>(null);
        var progressValue = UseState<int?>(null);
        var error = UseState<string?>(null);

        UseEffect(() =>
        {
            if (string.IsNullOrEmpty(selectedOwner.Value) && ghOwners.Value.Length > 0)
                selectedOwner.Set(ghOwners.Value[0]);
        }, EffectTrigger.OnMount(), ghOwners);

        UseEffect(() =>
        {
            selectedRepo.Set("");
        }, selectedOwner);

        UseEffect(() =>
        {
            var raw = projectName.Value ?? "";
            var sanitized = SanitizeProjectName(raw);
            if (sanitized != raw) projectName.Set(sanitized);
        }, projectName);

        Context.TryUseService<DesktopWindow>(out var desktop);
        var isDesktop = desktop != null;

        var repos = ghReposByOwner.Value.TryGetValue(selectedOwner.Value, out var ownerRepos)
            ? ownerRepos
            : Array.Empty<string>();

        // While cloning, replace the form with a full-width progress display.
        if (isCloning.Value)
        {
            return Layout.Vertical().Margin(0, 0, 0, 20).Gap(4)
                   | Text.Bold(progressMessage.Value ?? "Setting up your project...")
                   | (progressValue.Value != null
                       ? new Progress(progressValue.Value.Value)
                       : null!)
                   | (error.Value != null ? Text.Danger(error.Value) : null!);
        }

        var isRemote = mode.Value == "remote";

        object picker;
        if (isRemote)
        {
            picker = Layout.Horizontal().Gap(2).Width(Size.Full())
                | selectedOwner.ToSelectInput(ghOwners.Value)
                    .Placeholder("Owner")
                    .Width(Size.Fraction(0.3f))
                | selectedRepo.ToSelectInput(repos, disabled: string.IsNullOrEmpty(selectedOwner.Value))
                    .Placeholder("Select a repository")
                    .Width(Size.Grow())
                | new Button("Add").Icon(Icons.Plus)
                    .Disabled(string.IsNullOrEmpty(selectedRepo.Value))
                    .OnClick(() =>
                    {
                        var picked = selectedRepo.Value;
                        if (string.IsNullOrEmpty(picked) || string.IsNullOrEmpty(selectedOwner.Value))
                            return;

                        var owner = selectedOwner.Value;
                        var list = new List<RepoChoice>(selectedRepos.Value);
                        if (!list.Any(r => !r.IsLocal && r.Owner == owner && r.Name == picked))
                        {
                            list.Add(new RepoChoice(owner, picked));
                            selectedRepos.Set(list);

                            if (string.IsNullOrEmpty(projectName.Value))
                                projectName.Set(SanitizeProjectName(picked));
                        }
                        selectedRepo.Set("");
                    });
        }
        else if (isDesktop)
        {
            picker = Layout.Horizontal().Gap(2).Width(Size.Full())
                | newLocalPath.ToTextInput("Repository path")
                    .Width(Size.Grow())
                | new Button("Browse").Icon(Icons.FolderOpen).Outline()
                    .OnClick(() =>
                    {
                        var picked = desktop!.ShowSelectFolderDialog("Select repository folder");
                        if (picked != null && picked.Length > 0 && !string.IsNullOrEmpty(picked[0]))
                            newLocalPath.Set(picked[0]);
                    })
                | new Button("Add").Icon(Icons.Plus)
                    .Disabled(string.IsNullOrWhiteSpace(newLocalPath.Value))
                    .OnClick(() => AddLocalPath(selectedRepos, newLocalPath, projectName));
        }
        else
        {
            picker = Layout.Horizontal().Gap(2).Width(Size.Full())
                | newLocalPath.ToTextInput("Absolute path to local repository (e.g. /Users/you/code/myrepo)")
                    .Width(Size.Grow())
                | new Button("Add").Icon(Icons.Plus)
                    .Disabled(string.IsNullOrWhiteSpace(newLocalPath.Value))
                    .OnClick(() => AddLocalPath(selectedRepos, newLocalPath, projectName));
        }

        var sharedListLayout = Layout.Vertical().Gap(2);
        var current = selectedRepos.Value;
        for (var i = 0; i < current.Count; i++)
        {
            var idx = i;
            var item = current[idx];
            sharedListLayout |= new Box(
                Layout.Horizontal().Width(Size.Full()).AlignContent(Align.Center)
                | Text.Block(item.DisplayLabel)
                | (item.IsLocal
                    ? (object)new Badge("Local").Variant(BadgeVariant.Outline)
                    : new Badge("Remote").Variant(BadgeVariant.Outline))
                | new Spacer()
                | new Button().Icon(Icons.X).Ghost().OnClick(() =>
                {
                    var list = new List<RepoChoice>(selectedRepos.Value);
                    if (idx < list.Count) list.RemoveAt(idx);
                    selectedRepos.Set(list);
                }).WithTooltip("Remove")
            ).Padding(4, 2, 2, 2).Width(Size.Full());
        }

        var canContinue = selectedRepos.Value.Count > 0
                          && !string.IsNullOrWhiteSpace(projectName.Value);

        // Remote | toggle | Local — single left-aligned row.
        var modeToggle = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                         | Text.Block("Remote")
                         | new ConvertedState<string, bool>(
                                 mode,
                                 m => m == "local",
                                 v => v ? "local" : "remote")
                             .ToSwitchInput()
                         | Text.Block("Local");

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2("Setup your first project")
               | Text.Muted(isRemote
                   ? "Pick the GitHub repositories this project will work with."
                   : (isDesktop
                       ? "Select folders containing local git repositories."
                       : "Add absolute paths to local git repositories on the server."))
               | modeToggle
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | picker
               | (selectedRepos.Value.Count > 0 ? new Separator() : null!)
               | (selectedRepos.Value.Count > 0 ? sharedListLayout : null!)
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
                              foreach (var choice in selectedRepos.Value)
                              {
                                  i++;
                                  if (choice.IsLocal)
                                  {
                                      progressMessage.Set($"Adding {choice.LocalPath} ({i}/{total})...");
                                      var trimmed = (choice.LocalPath ?? "").Trim();
                                      if (!string.IsNullOrWhiteSpace(trimmed))
                                          refs.Add(new RepoRef { Path = trimmed, PrRule = "default" });
                                  }
                                  else
                                  {
                                      Directory.CreateDirectory(reposDir);
                                      progressMessage.Set($"Fetching {choice.Owner}/{choice.Name} ({i}/{total})...");
                                      var url = $"https://github.com/{choice.Owner}/{choice.Name}.git";
                                      var destPath = Path.Combine(reposDir, choice.Name);
                                      var success = await GitHubCliHelper.CloneRepositoryAsync(url, destPath);
                                      if (!success)
                                      {
                                          progressCts.Cancel();
                                          progressValue.Set(null);
                                          progressMessage.Set(null);
                                          error.Set($"Failed to fetch repository: {choice.Owner}/{choice.Name}.");
                                          isCloning.Set(false);
                                          return;
                                      }
                                      refs.Add(new RepoRef { Path = destPath, PrRule = "default" });
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

    private static void AddLocalPath(
        IState<List<RepoChoice>> selectedRepos,
        IState<string> newLocalPath,
        IState<string> projectName)
    {
        var path = (newLocalPath.Value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path)) return;

        var list = new List<RepoChoice>(selectedRepos.Value);
        if (!list.Any(r => r.IsLocal && r.LocalPath == path))
        {
            var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(leaf)) leaf = path;
            list.Add(new RepoChoice("", leaf, IsLocal: true, LocalPath: path));
            selectedRepos.Set(list);

            if (string.IsNullOrEmpty(projectName.Value) && !string.IsNullOrEmpty(leaf))
                projectName.Set(SanitizeProjectName(leaf));
        }
        newLocalPath.Set("");
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
