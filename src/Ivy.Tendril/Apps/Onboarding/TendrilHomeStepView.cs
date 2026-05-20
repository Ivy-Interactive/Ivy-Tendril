using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public class TendrilHomeStepView(
    IState<int> stepperIndex,
    IState<string> tendrilHomePath,
    IState<bool> homeBootstrapped,
    IState<bool> isStepLoading) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var setupService = UseService<IOnboardingSetupService>();

        var error = UseState<string?>(null);
        var isBootstrapping = UseState(false);

        var defaultHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".tendril");

        return Layout.Vertical().Margin(0, 0, 0, 20)
               | Text.H3("Where should we store your data?")
               | Text.Muted(
                   """
                   Tendril keeps your config and plans in this folder.
                   """)
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | tendrilHomePath.ToTextInput(defaultHome)
                   .WithField().Required().Label("Tendril Home")
               | new Spacer()
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back")
                      .Outline()
                      .Icon(Icons.ArrowLeft)
                      .Large()
                      .Disabled(isBootstrapping.Value)
                      .OnClick(() => stepperIndex.Set(stepperIndex.Value - 1))
                  | new Spacer()
                  | new Button("Next")
                      .Primary()
                      .Icon(Icons.ArrowRight, Align.Right)
                      .Large()
                      .Disabled(isBootstrapping.Value || string.IsNullOrWhiteSpace(tendrilHomePath.Value))
                      .Loading(isBootstrapping.Value)
                      .OnClick(OnNext));

        async ValueTask OnNext()
        {
            if (string.IsNullOrWhiteSpace(tendrilHomePath.Value))
            {
                error.Set("Please provide a valid path.");
                return;
            }

            string resolved;
            try
            {
                resolved = PathHelper.ResolvePath(tendrilHomePath.Value);
            }
            catch (Exception ex)
            {
                error.Set($"Invalid path: {ex.Message}");
                return;
            }

            error.Set(null);
            isBootstrapping.Set(true);
            isStepLoading.Set(true);
            try
            {
                config.SetPendingTendrilHome(resolved);
                await setupService.BootstrapTendrilHomeAsync(resolved);
                homeBootstrapped.Set(true);
                tendrilHomePath.Set(resolved);
                stepperIndex.Set(stepperIndex.Value + 1);
            }
            catch (Exception ex)
            {
                error.Set($"Failed to set up data folder: {ex.Message}");
            }
            finally
            {
                isBootstrapping.Set(false);
                isStepLoading.Set(false);
            }
        }
    }

}
