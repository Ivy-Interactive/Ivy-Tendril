using Ivy.Tendril.Services;
using Ivy.Widgets.ClaudeJsonRenderer;

namespace Ivy.Tendril.Apps.Onboarding;

public class CompleteStepView(
    IState<int> stepperIndex,
    IState<List<RepoRef>> selectedRepos,
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
        var hasOutput = UseState(false);
        var error = UseState<string?>(null);
        var refreshToken = UseState(0);
        var isFinishing = UseState(false);

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

                var notifyingStream = new NotifyingStream<string>(stream, () => hasOutput.Set(true));

                foreach (var name in projectsNeedingVerifications)
                {
                    var handle = runner.Run(new PromptwareRunOptions
                    {
                        Promptware = "UpdateProject",
                        Values = new()
                        {
                            ["ProjectName"] = name,
                            ["Instructions"] = "Setup verifications"
                        }
                    }, notifyingStream);

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
            ? "Setting up verifications…"
            : "Ready to Go!";

        var subText = running.Value
            ? "Tendril is detecting your tech stack and configuring verifications."
            : $"{totalVerifications} verification(s) configured across {projects.Count} project(s). Click Finish to start using Tendril, or go back to add another project.";

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H2(headerText)
               | Text.Muted(subText)
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | (running.Value
                   ? (object)new Box(
                       Layout.Vertical().Gap(4).Width(Size.Full()).Height(Size.Full())
                       | (!hasOutput.Value
                           ? (object)(Layout.Vertical().Gap(2).AlignContent(Align.Center).Width(Size.Full()).Padding(8)
                               | Icons.LoaderCircle.ToIcon().WithAnimation(AnimationType.Rotate).Duration(1)
                               | Text.Muted("Starting agent..."))
                           : null!)
                       | new ClaudeJsonRenderer()
                           .Stream(stream)
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
               | (!running.Value && totalVerifications > 0 ? (object)listLayout : null!)
               | (Layout.Horizontal().Width(Size.Full())
                  | new Spacer()
                  | new Button("Finish").Primary().Large().Icon(Icons.Check, Align.Right)
                      .Disabled(running.Value || isFinishing.Value)
                      .Loading(isFinishing.Value)
                      .OnClick(async () => await OnFinish()));
    }

    private class NotifyingStream<T> : IWriteStream<T>
    {
        private readonly IWriteStream<T> _inner;
        private readonly Action _onFirstWrite;
        private bool _notified;

        public NotifyingStream(IWriteStream<T> inner, Action onFirstWrite)
        {
            _inner = inner;
            _onFirstWrite = onFirstWrite;
        }

        public string Id => _inner.Id;

        public void Write(T data)
        {
            if (!_notified)
            {
                _notified = true;
                _onFirstWrite();
            }
            _inner.Write(data);
        }
    }
}
