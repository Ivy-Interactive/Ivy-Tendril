using Ivy.Tendril.Services;
using Ivy.Widgets.ClaudeJsonRenderer;

namespace Ivy.Tendril.Apps.Onboarding;

public class CompleteStepView(IState<int> stepperIndex) : ViewBase
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

        UseEffect(async () =>
        {
            var pendingProject = config.GetPendingProject();
            if (pendingProject == null)
            {
                error.Set("No project found from the previous step.");
                running.Set(false);
                return;
            }

            try
            {
                await setupService.CommitPendingProjectAsync();

                var existing = config.Settings.Projects
                    .FirstOrDefault(p => p.Name.Equals(pendingProject.Name, StringComparison.OrdinalIgnoreCase));
                if (existing?.Verifications.Count > 0)
                {
                    running.Set(false);
                    return;
                }

                var handle = runner.Run(new PromptwareRunOptions
                {
                    Promptware = "UpdateProject",
                    Values = new()
                    {
                        ["ProjectName"] = pendingProject.Name,
                        ["Instructions"] = "Setup verifications"
                    }
                }, stream);

                await handle.Completion;
                config.ReloadSettings();
                refreshToken.Set(refreshToken.Value + 1);
            }
            catch (Exception ex)
            {
                error.Set($"Verification setup failed: {ex.Message}");
            }
            finally
            {
                running.Set(false);
            }
        }, [EffectTrigger.OnMount()]);

        var projectName = config.GetPendingProject()?.Name ?? "";

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

        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
        var verifications = project?.Verifications ?? new List<ProjectVerificationRef>();

        var listLayout = Layout.Vertical().Gap(2);
        for (var i = 0; i < verifications.Count; i++)
        {
            var idx = i;
            var v = verifications[idx];
            listLayout |= new Box(
                Layout.Horizontal().Width(Size.Full()).AlignContent(Align.Center)
                | Text.Block(v.Name)
                | (v.Required ? (object)new Badge("required").Variant(BadgeVariant.Outline) : null!)
                | new Spacer()
                | new Button().Icon(Icons.X).Ghost().OnClick(async () =>
                {
                    await setupService.RemoveProjectVerificationAsync(projectName, v.Name);
                    refreshToken.Set(refreshToken.Value + 1);
                }).WithTooltip("Remove")
            ).Padding(4, 2, 2, 2).Width(Size.Full());
        }

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2(running.Value ? "Setting up verifications…" : "Ready to Go!")
               | Text.Muted(running.Value
                   ? "Tendril is detecting your tech stack and configuring verifications for this project."
                   : $"{verifications.Count} verification(s) configured. Click **Finish** to start using Tendril.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | (running.Value
                   ? (object)new Box(
                       new ClaudeJsonRenderer()
                           .Stream(stream)
                           .ShowThinking(false)
                           .ShowSystemEvents(false)
                           .AutoScroll(true)
                           .Height(Size.Full())
                     )
                       .Width(Size.Full())
                       .Height(Size.Units(120).Max(Size.Fraction(0.6f)))
                       .Padding(0)
                   : null!)
               | (!running.Value && verifications.Count > 0 ? (object)listLayout : null!)
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                      .Disabled(running.Value || isFinishing.Value)
                      .OnClick(() => stepperIndex.Set(stepperIndex.Value - 1))
                  | new Spacer()
                  | new Button("Finish").Primary().Large().Icon(Icons.Check, Align.Right)
                      .Disabled(running.Value || isFinishing.Value)
                      .Loading(isFinishing.Value)
                      .OnClick(async () => await OnFinish()));
    }
}
