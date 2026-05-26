using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Setup;

public class AppearanceSetupView : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();

        return Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Appearance").Bold()
               | Text.Muted("Choose how Tendril appears. System matches your OS setting.").Small()
               | (Layout.Horizontal().Gap(2)
                  | new Button("Light").Variant(ButtonVariant.Outline).Icon(Icons.Sun)
                      .OnClick(() => client.SetThemeMode(ThemeMode.Light))
                  | new Button("Dark").Variant(ButtonVariant.Outline).Icon(Icons.Moon)
                      .OnClick(() => client.SetThemeMode(ThemeMode.Dark))
                  | new Button("System").Variant(ButtonVariant.Outline).Icon(Icons.SunMoon)
                      .OnClick(() => client.SetThemeMode(ThemeMode.System)));
    }
}
