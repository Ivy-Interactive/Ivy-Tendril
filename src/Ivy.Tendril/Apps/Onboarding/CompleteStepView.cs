using Ivy.Tendril.Services;
using Ivy.Widgets.ClaudeJsonRenderer;

namespace Ivy.Tendril.Apps.Onboarding;

public class CompleteStepView(
    IState<int> stepperIndex,
    IState<string> selectedOwner,
    IState<List<RepoChoice>> selectedRepos,
    IState<string> projectName) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var setupService = UseService<IOnboardingSetupService>();
        var runner = UseService<IPromptwareRunner>();
        var client = UseService<IClientProvider>();

        var stream = UseStream<string>();
        var running = UseState(true);
        var error = UseState<string?>(null);
        var refreshToken = UseState(0);
        var isFinishing = UseState(false);
        var currentProject = UseState<string?>((string?)null);

        UseEffect(async () =>
        {
            try
            {
                await setupService.CommitPendingProjectAsync();

                var projectsNeedingVerifications = config.Settings.Projects
                    .Where(p => p.Verifications == null || p.Verifications.Count == 0)
                    .Select(p => p.Name)
                    .ToList();

                if (projectsNeedingVerifications.Count == 0 && config.Settings.Projects.Count == 0)
                {
                    error.Set("No project found from the previous step.");
                    running.Set(false);
                    return;
                }

                foreach (var name in projectsNeedingVerifications)
                {
                    currentProject.Set(name);
                    var handle = runner.Run(new PromptwareRunOptions
                    {
                        Promptware = "UpdateProject",
                        Values = new()
                        {
                            ["ProjectName"] = name,
                            ["Instructions"] = "Setup verifications"
                        }
                    }, stream);

                    await handle.Completion;
                    config.ReloadSettings();
                    refreshToken.Set(refreshToken.Value + 1);
                }
            }
            catch (Exception ex)
            {
                error.Set($"Verification setup failed: {ex.Message}");
            }
            finally
            {
                currentProject.Set((string?)null);
                running.Set(false);
            }
        }, [EffectTrigger.OnMount()]);

        async Task OnFinish()
        {
            isFinishing.Set(true);
            error.Set(null);
            try
            {
                await setupService.FinalizeOnboardingAsync();
                await setupService.StartBackgroundServicesAsync();
                client.ReloadPage();
            }
            catch (Exception ex)
            {
                error.Set($"Failed to complete setup: {ex.Message}");
                isFinishing.Set(false);
            }
        }

        void OnBack()
        {
            selectedRepos.Set(new List<RepoChoice>());
            projectName.Set("");
            selectedOwner.Set("");
            stepperIndex.Set(stepperIndex.Value - 1);
        }

        var projects = config.Settings.Projects;
        var totalVerifications = projects.Sum(p => p.Verifications?.Count ?? 0);

        var listLayout = Layout.Vertical().Gap(2);
        foreach (var project in projects)
        {
            var verifications = project.Verifications ?? new List<ProjectVerificationRef>();
            if (verifications.Count == 0) continue;

            listLayout |= Text.Block($"**{project.Name}**");
            foreach (var v in verifications)
            {
                var capturedProjectName = project.Name;
                var capturedVerificationName = v.Name;
                listLayout |= new Box(
                    Layout.Horizontal().Width(Size.Full()).AlignContent(Align.Center)
                    | Text.Block(v.Name)
                    | (v.Required ? (object)new Badge("required").Variant(BadgeVariant.Outline) : null!)
                    | new Spacer()
                    | new Button().Icon(Icons.X).Ghost().OnClick(async () =>
                    {
                        await setupService.RemoveProjectVerificationAsync(capturedProjectName, capturedVerificationName);
                        refreshToken.Set(refreshToken.Value + 1);
                    }).WithTooltip("Remove")
                ).Padding(4, 2, 2, 2).Width(Size.Full());
            }
        }

        var headerText = running.Value
            ? (currentProject.Value != null
                ? $"Setting up verifications for {currentProject.Value}…"
                : "Setting up verifications…")
            : "Ready to Go!";

        var subText = running.Value
            ? "Tendril is detecting your tech stack and configuring verifications."
            : $"{totalVerifications} verification(s) configured across {projects.Count} project(s). Click **Finish** to start using Tendril, or go back to add another project.";

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2(headerText)
               | Text.Muted(subText)
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | (running.Value
                   ? (object)new Box(
                       new ClaudeJsonRenderer()
                           .Stream(stream)
                           .ShowThinking(false)
                           .ShowSystemEvents(false)
                           .AutoScroll(true)
                           .Width(Size.Full())
                           .Height(Size.Full())
                     )
                       .Width(Size.Full())
                       .Height(Size.Units(120).Max(Size.Fraction(0.6f)))
                       .Padding(0)
                   : null!)
               | (!running.Value && totalVerifications > 0 ? (object)listLayout : null!)
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                      .Disabled(running.Value || isFinishing.Value)
                      .OnClick(OnBack)
                  | new Spacer()
                  | new Button("Finish").Primary().Large().Icon(Icons.Check, Align.Right)
                      .Disabled(running.Value || isFinishing.Value)
                      .Loading(isFinishing.Value)
                      .OnClick(async () => await OnFinish()));
    }
}
