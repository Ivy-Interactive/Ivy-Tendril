using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class AppearanceSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();

        var isSidebarOpen = config.Settings.SidebarOpen;

        return Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Appearance").Bold()
               | Text.Muted("Choose how Tendril appears. System matches your OS setting.").Small()
               | (Layout.Horizontal()
                  | new Button("Light").Variant(ButtonVariant.Outline).Icon(Icons.Sun)
                      .OnClick(() => client.SetThemeMode(ThemeMode.Light))
                  | new Button("Dark").Variant(ButtonVariant.Outline).Icon(Icons.Moon)
                      .OnClick(() => client.SetThemeMode(ThemeMode.Dark))
                  | new Button("System").Variant(ButtonVariant.Outline).Icon(Icons.SunMoon)
                      .OnClick(() => client.SetThemeMode(ThemeMode.System)))
               | Text.Block("Main Sidebar").Bold()
               | Text.Muted("Choose the default state for the main sidebar on startup.").Small()
               | (Layout.Horizontal()
                  | new Button("Expanded")
                      .Variant(isSidebarOpen ? ButtonVariant.Primary : ButtonVariant.Outline)
                      .Icon(Icons.PanelLeftOpen)
                      .OnClick(() =>
                      {
                          config.Settings.SidebarOpen = true;
                          config.SaveSettings();
                          client.Toast("Sidebar set to expanded by default", "Saved");
                      })
                  | new Button("Collapsed")
                      .Variant(!isSidebarOpen ? ButtonVariant.Primary : ButtonVariant.Outline)
                      .Icon(Icons.PanelLeftClose)
                      .OnClick(() =>
                      {
                          config.Settings.SidebarOpen = false;
                          config.SaveSettings();
                          client.Toast("Sidebar set to collapsed by default", "Saved");
                      }));
    }
}
