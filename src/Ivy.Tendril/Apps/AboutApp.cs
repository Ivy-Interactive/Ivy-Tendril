using Ivy.Tendril.Apps.Settings;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

[App(title: "About", icon: Icons.Info, isVisible: false)]
public class AboutApp : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        Context.TryUseService<TendrilArgs>(out var tendrilArgs);
        var navigator = UseNavigation();

        var isBeta = BetaHelper.IsBeta(tendrilArgs, config);
        if (!isBeta)
        {
            var button = new Button("Open Advanced Settings")
                .Icon(Icons.Cog)
                .OnClick(() => navigator.Navigate<SettingsApp>(new SettingsAppArgs(Section: "advanced")));

            return new NoContentView(
                "Beta Feature",
                "The About page is a beta feature. You can enable beta features in Advanced Settings.",
                button);
        }

        return new AboutSetupView();
    }
}
