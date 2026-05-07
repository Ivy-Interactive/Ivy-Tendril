using Ivy.Tendril.Services;
using Ivy.Widgets.ClaudeJsonRenderer;

namespace Ivy.Tendril.Apps.Onboarding;

public class CompleteStepView(
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
        var client = UseService<IClientProvider>();

        var isFinishing = UseState(false);

        UseEffect(async () =>
        {
            if (session.Started.Value) return;

            try
            {
                await setupService.CommitPendingProjectAsync();

                var projectsNeedingVerifications = config.Settings.Projects
                    .Where(p => p.Verifications == null || p.Verifications.Count == 0)
                    .Select(p => p.Name)
                    .ToList();

                if (projectsNeedingVerifications.Count == 0 && config.Settings.Projects.Count == 0)
                {
                    session.Error.Set("No project found from the previous step.");
                    session.Running.Set(false);
                    return;
                }

                var notifyingStream = new NotifyingStream<string>(
                    session.Stream,
                    () => session.HasOutput.Set(true));

                session.Started.Set(true);
                session.Running.Set(true);

                foreach (var name in projectsNeedingVerifications)
                {
                    if (session.Cancelled.Value) break;

                    var handle = runner.Run(new PromptwareRunOptions
                    {
                        Promptware = "UpdateProject",
                        Values = new()
                        {
                            ["ProjectName"] = name,
                            ["Instructions"] = "Setup verifications"
                        }
                    }, notifyingStream);

                    session.Handle.Set(handle);

                    try
                    {
                        await handle.Completion;
                    }
                    catch (OperationCanceledException) { }

                    session.Handle.Set((PromptwareRunHandle?)null);

                    if (session.Cancelled.Value) break;

                    config.ReloadSettings();
                    session.RefreshToken.Set(session.RefreshToken.Value + 1);
                }
            }
            catch (Exception ex)
            {
                if (!session.Cancelled.Value)
                    session.Error.Set($"Verification setup failed: {ex.Message}");
            }
            finally
            {
                session.Running.Set(false);
            }
        }, [EffectTrigger.OnMount()]);

        void OnBack()
        {
            session.Reset();
            stepperIndex.Set(stepperIndex.Value - 1);
        }

        async Task OnFinish()
        {
            isFinishing.Set(true);
            session.Error.Set(null);
            try
            {
                await setupService.FinalizeOnboardingAsync();
                await setupService.StartBackgroundServicesAsync();
                client.ReloadPage();
            }
            catch (Exception ex)
            {
                session.Error.Set($"Failed to complete setup: {ex.Message}");
                isFinishing.Set(false);
            }
        }

        var projects = config.Settings.Projects;
        var totalVerifications = projects.Sum(p => p.Verifications?.Count ?? 0);

        var listLayout = Layout.Vertical().Gap(2);
        foreach (var project in projects)
        {
            var verifications = project.Verifications ?? new List<ProjectVerificationRef>();
            if (verifications.Count == 0) continue;

            listLayout |= Text.Bold(project.Name);
            foreach (var v in verifications)
            {
                var capturedProjectName = project.Name;
                var capturedVerificationName = v.Name;
                var prompt = config.Settings.Verifications
                    .FirstOrDefault(vc => vc.Name == v.Name)?.Prompt ?? "";

                var header = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.Center)
                    | Text.Block(v.Name)
                    | (v.Required ? (object)new Badge("required").Variant(BadgeVariant.Outline) : null!)
                    | new Spacer()
                    | new Button().Icon(Icons.X).Ghost().OnClick(async () =>
                    {
                        await setupService.RemoveProjectVerificationAsync(capturedProjectName, capturedVerificationName);
                        session.RefreshToken.Set(session.RefreshToken.Value + 1);
                    }).WithTooltip("Remove");

                listLayout |= new Expandable(header, Text.Muted(prompt))
                    .Ghost()
                    .Width(Size.Full());
            }
        }

        var running = session.Running.Value;
        var hasOutput = session.HasOutput.Value;
        var error = session.Error.Value;

        var headerText = running
            ? "Setting up verifications…"
            : "Ready to Go!";

        var subText = running
            ? "Tendril is detecting your tech stack and configuring verifications."
            : $"{totalVerifications} verification(s) configured across {projects.Count} project(s). Click Finish to start using Tendril, or go back to add another project.";

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2(headerText)
               | Text.Muted(subText)
               | (error != null ? Text.Danger(error) : null!)
               | (running
                   ? (object)new Box(
                       Layout.Vertical().Gap(4).Width(Size.Full()).Height(Size.Full())
                       | (!hasOutput
                           ? (object)(Layout.Vertical().Gap(2).AlignContent(Align.Center).Width(Size.Full()).Padding(8)
                               | Icons.LoaderCircle.ToIcon().WithAnimation(AnimationType.Rotate).Duration(1)
                               | Text.Muted("Starting agent..."))
                           : null!)
                       | new ClaudeJsonRenderer()
                           .Stream(session.Stream)
                           .ShowThinking(false)
                           .ShowSystemEvents(false)
                           .AutoScroll(true)
                           .Width(Size.Full())
                           .Height(Size.Full())
                     )
                       .Width(Size.Full())
                       .Height(Size.Units(100).Max(Size.Fraction(0.6f)))
                       .Padding(0)
                   : null!)
               | (!running && totalVerifications > 0 ? (object)listLayout : null!)
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back").Outline().Icon(Icons.ArrowLeft)
                      .Disabled(isFinishing.Value)
                      .OnClick(OnBack)
                  | new Spacer()
                  | new Button("Finish").Primary().Large().Icon(Icons.Check, Align.Right)
                      .Disabled(running || isFinishing.Value)
                      .Loading(isFinishing.Value)
                      .OnClick(async () => await OnFinish()));
    }
}
