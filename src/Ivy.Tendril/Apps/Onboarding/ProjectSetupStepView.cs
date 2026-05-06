using Ivy.Desktop;
using Ivy.Tendril.Apps.Onboarding.Dialogs;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectSetupStepView(IState<int> stepperIndex) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var projectName = UseState("");
        var repoPaths = UseState(new List<string> { "" });
        var projectContext = UseState("");
        var error = UseState<string?>(null);
        var verifications = UseState(new List<VerificationEntry>
        {
            new("CheckResult", "Verify the implementation matches the plan requirements.", true)
        });

        // Dialog state for editing verifications
        var editIndex = UseState<int?>(-1); // -1 = closed, null = new, >= 0 = editing index

        var reposLayout = Layout.Vertical().Gap(2);
        var currentRepos = repoPaths.Value;
        for (var i = 0; i < currentRepos.Count; i++)
        {
            var ri = i;
            reposLayout |= new RepoPathInputView(repoPaths, ri, () =>
                           {
                               var list = new List<string>(repoPaths.Value);
                               if (ri < list.Count) list.RemoveAt(ri);
                               if (list.Count == 0) list.Add("");
                               repoPaths.Set(list);
                           });
        }



        // Verification list
        var verificationsLayout = Layout.Vertical().Gap(2);
        var currentVerifications = verifications.Value;
        for (var i = 0; i < currentVerifications.Count; i++)
        {
            var vi = i;
            var v = currentVerifications[vi];
            verificationsLayout |= Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                                   | Text.Block(v.Name).Width(Size.Grow())
                                   | (v.Required ? new Badge("Required") : null!)
                                   | new Button().Icon(Icons.Pencil).Ghost().OnClick(() =>
                                   {
                                       editIndex.Set(vi);
                                   })
                                   | new Button().Icon(Icons.Trash).Ghost().OnClick(() =>
                                   {
                                       var list = new List<VerificationEntry>(verifications.Value);
                                       list.RemoveAt(vi);
                                       verifications.Set(list);
                                   });
        }

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2("Setup your first project")
               | Text.Muted("Set up your first project. You can add more projects later in Settings.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | projectContext.ToTextareaInput()
                   .Rows(4)
                   .WithField()
                   .Label("Context (Optional)")
               | new Separator()
               | (Layout.Vertical().Gap(2)
                  | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                     | Text.Block("Repositories").Bold()
                     | new Button("Add Repository").Icon(Icons.Plus).Outline().OnClick(() =>
                     {
                         var list = new List<string>(repoPaths.Value) { "" };
                         repoPaths.Set(list);
                     }))
                  | Text.Muted("Add at least one repository URL for this project.")
                  | reposLayout)
               | new Separator()
               | (Layout.Vertical().Gap(2)
                  | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                     | Text.Block("Verifications").Bold()
                     | new Button("Add Verification").Icon(Icons.Plus).Outline().OnClick(() =>
                     {
                         editIndex.Set(null);
                     }))
                  | Text.Muted("Define verifications to run for this project.")
                  | verificationsLayout)
               | new Separator()
               | (Layout.Horizontal().Gap(2)
                  | new Button("Next").Primary().Large().Icon(Icons.ArrowRight, Align.Right)
                      .OnClick(() =>
                      {
                          if (string.IsNullOrWhiteSpace(projectName.Value))
                          {
                              error.Set("Please enter a project name.");
                              return;
                          }

                          var filledRepos = repoPaths.Value.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                          if (filledRepos.Count == 0)
                          {
                              error.Set("Please add at least one repository URL.");
                              return;
                          }

                          var validVerifications = verifications.Value
                              .Where(v => !string.IsNullOrWhiteSpace(v.Name))
                              .ToList();

                          var project = new ProjectConfig
                          {
                              Name = projectName.Value.Trim(),
                              Color = "Green",
                              Repos = repoPaths.Value
                                  .Where(p => !string.IsNullOrWhiteSpace(p))
                                  .Select(p => new RepoRef { Path = p, PrRule = "default" })
                                  .ToList(),
                              Context = projectContext.Value.Trim(),
                              Verifications = validVerifications.Select(v => new ProjectVerificationRef
                              {
                                  Name = v.Name,
                                  Required = v.Required
                              }).ToList()
                          };

                          config.SetPendingProject(project);
                          config.SetPendingVerificationDefinitions(validVerifications
                              .Select(v => new VerificationConfig
                              {
                                  Name = v.Name,
                                  Prompt = v.Prompt
                              }).ToList());

                          error.Set(null);
                          stepperIndex.Set(stepperIndex.Value + 1);
                      })
               )
               | new EditOnboardingVerificationDialog(editIndex, verifications);
    }
}

internal record VerificationEntry(string Name, string Prompt, bool Required);

internal class RepoPathInputView(IState<List<string>> repoPaths, int index, Action onRemove) : ViewBase
{
    public override object Build()
    {
        var repoUrl = UseState(repoPaths.Value[index]);
        var usePicker = UseState(false);
        var loading = UseState(false);
        
        var owners = UseState<string[]>([]);
        var repos = UseState<string[]>([]);
        var branches = UseState<string[]>([]);
        
        var selectedOwner = UseState<string>("");
        var selectedRepo = UseState<string>("");
        var selectedBranch = UseState<string>("");
        
        UseEffect(() =>
        {
            var list = new List<string>(repoPaths.Value);
            list[index] = repoUrl.Value ?? "";
            repoPaths.Set(list);
        }, repoUrl);

        UseEffect(async () =>
        {
            if (usePicker.Value && owners.Value.Length == 0)
            {
                loading.Set(true);
                var res = await GitHubCliHelper.GetOwnersAsync();
                owners.Set(res);
                if (res.Length > 0) selectedOwner.Set(res[0]);
                loading.Set(false);
            }
        }, usePicker);

        UseEffect(async () =>
        {
            selectedRepo.Set("");
            repos.Set([]);
            if (!string.IsNullOrEmpty(selectedOwner.Value))
            {
                loading.Set(true);
                var res = await GitHubCliHelper.GetRepositoriesAsync(selectedOwner.Value);
                repos.Set(res);
                if (res.Length > 0) selectedRepo.Set(res[0]);
                else selectedRepo.Set("");
                loading.Set(false);
            }
        }, selectedOwner);

        UseEffect(async () =>
        {
            selectedBranch.Set("");
            branches.Set([]);
            if (!string.IsNullOrEmpty(selectedOwner.Value) && !string.IsNullOrEmpty(selectedRepo.Value))
            {
                loading.Set(true);
                var res = await GitHubCliHelper.GetBranchesAsync(selectedOwner.Value, selectedRepo.Value);
                branches.Set(res);
                if (res.Length > 0) selectedBranch.Set(res[0]);
                else selectedBranch.Set("");
                loading.Set(false);
            }
        }, selectedRepo);

        UseEffect(() =>
        {
            if (usePicker.Value && !string.IsNullOrEmpty(selectedOwner.Value) && !string.IsNullOrEmpty(selectedRepo.Value))
            {
                var url = $"https://github.com/{selectedOwner.Value}/{selectedRepo.Value}.git";
                repoUrl.Set(url);
            }
        }, selectedOwner, selectedRepo, selectedBranch);

        var rightControls = Layout.Horizontal().Gap(2).AlignContent(Align.Right)
                     | new Icon(Icons.Github)
                     | usePicker.ToSwitchInput(label: "Browser")
                     | new Button().Icon(Icons.Trash).Ghost().OnClick(onRemove).WithTooltip("Remove repository");

        if (usePicker.Value)
        {
            return Layout.Vertical().Gap(2).Width(Size.Grow())
                   | (Layout.Horizontal().Gap(2).Width(Size.Grow())
                      | selectedOwner.ToSelectInput(owners.Value, disabled: loading.Value).Width(Size.Grow())
                      | selectedRepo.ToSelectInput(repos.Value, disabled: loading.Value).Width(Size.Grow())
                      | selectedBranch.ToSelectInput(branches.Value, disabled: loading.Value).Width(Size.Grow())
                      | rightControls)
                   | (loading.Value ? Text.Muted("Loading GitHub data...") : null!);
        }

        return Layout.Horizontal().Gap(2).Width(Size.Grow())
               | repoUrl.ToTextInput("Repository URL (e.g. https://github.com/owner/repo.git)")
                   .Width(Size.Grow())
               | rightControls;
    }
}
