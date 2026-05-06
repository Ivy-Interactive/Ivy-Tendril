using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public record RepoChoice(string Owner, string Name);

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

        var selectedRepo = UseState("");
        var isCloning = UseState(false);
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

        var repos = ghReposByOwner.Value.TryGetValue(selectedOwner.Value, out var ownerRepos)
            ? ownerRepos
            : Array.Empty<string>();

        var listLayout = Layout.Vertical().Gap(2);
        var current = selectedRepos.Value;
        for (var i = 0; i < current.Count; i++)
        {
            var idx = i;
            var item = current[idx];
            listLayout |= new Box(
                Layout.Horizontal().Width(Size.Full()).AlignContent(Align.Center)
                | Text.Block($"{item.Owner}/{item.Name}")
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
                          && !string.IsNullOrWhiteSpace(projectName.Value)
                          && !isCloning.Value;

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2("Setup your first project")
               | Text.Muted("Pick the repositories this project will work with.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | (Layout.Horizontal().Gap(2).Width(Size.Full())
                  | selectedOwner.ToSelectInput(ghOwners.Value, disabled: isCloning.Value)
                       .Placeholder("Owner")
                       .Width(Size.Fraction(0.3f))
                  | selectedRepo.ToSelectInput(repos, disabled: isCloning.Value || string.IsNullOrEmpty(selectedOwner.Value))
                       .Placeholder("Select a repository")
                       .Width(Size.Grow())
                  | new Button("Add").Icon(Icons.Plus)
                       .Disabled(string.IsNullOrEmpty(selectedRepo.Value) || isCloning.Value)
                       .OnClick(() =>
                       {
                           var picked = selectedRepo.Value;
                           if (string.IsNullOrEmpty(picked) || string.IsNullOrEmpty(selectedOwner.Value))
                               return;

                           var owner = selectedOwner.Value;
                           var list = new List<RepoChoice>(selectedRepos.Value);
                           if (!list.Any(r => r.Owner == owner && r.Name == picked))
                           {
                               list.Add(new RepoChoice(owner, picked));
                               selectedRepos.Set(list);

                               if (string.IsNullOrEmpty(projectName.Value))
                                   projectName.Set(SanitizeProjectName(picked));
                           }
                           selectedRepo.Set("");
                       }))
               | new Separator()
               | (selectedRepos.Value.Count > 0 ? listLayout : null!)
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                      .Disabled(isCloning.Value)
                      .OnClick(() => stepperIndex.Set(stepperIndex.Value - 1))
                  | new Spacer()
                  | new Button("Continue").Primary().Large().Icon(Icons.ArrowRight, Align.Right)
                      .Disabled(!canContinue)
                      .Loading(isCloning.Value)
                      .OnClick(async () =>
                      {
                          if (selectedRepos.Value.Count == 0)
                          {
                              error.Set("Please select at least one repository.");
                              return;
                          }
                          var name = SanitizeProjectName(projectName.Value);
                          if (string.IsNullOrWhiteSpace(name))
                          {
                              error.Set("Please enter a valid project name.");
                              return;
                          }

                          isCloning.Set(true);
                          error.Set(null);

                          var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")
                                            ?? Path.Combine(
                                                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                                ".tendril");
                          var reposDir = Path.Combine(tendrilHome, "repos");
                          Directory.CreateDirectory(reposDir);

                          var refs = new List<RepoRef>();
                          foreach (var choice in selectedRepos.Value)
                          {
                              var url = $"https://github.com/{choice.Owner}/{choice.Name}.git";
                              var destPath = Path.Combine(reposDir, choice.Name);
                              var success = await GitHubCliHelper.CloneRepositoryAsync(url, destPath);
                              if (!success)
                              {
                                  error.Set($"Failed to fetch repository: {choice.Owner}/{choice.Name}.");
                                  isCloning.Set(false);
                                  return;
                              }
                              refs.Add(new RepoRef { Path = destPath, PrRule = "default" });
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

                          isCloning.Set(false);
                          stepperIndex.Set(stepperIndex.Value + 1);
                      }));
    }

    private static string SanitizeProjectName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Regex.Replace(input, @"[^A-Za-z0-9._-]", "");
    }
}
