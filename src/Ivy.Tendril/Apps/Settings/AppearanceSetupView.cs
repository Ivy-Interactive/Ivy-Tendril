using Ivy.Tendril.Services;
using Ivy.Tendril.Themes;

namespace Ivy.Tendril.Apps.Settings;

public class AppearanceSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var themeMode = UseState(() => config.Settings.ThemeMode ?? "system");
        var selectedTheme = UseState(() => config.Settings.Theme ?? "default");
        var lastAppliedTheme = UseState(() => config.Settings.Theme ?? "default");

        UseEffect(() =>
        {
            var themeId = selectedTheme.Value;
            if (string.IsNullOrWhiteSpace(themeId)) themeId = "default";

            if (themeId == lastAppliedTheme.Value) return;

            lastAppliedTheme.Set(themeId);
            var theme = TendrilThemes.GetTheme(themeId);
            TendrilThemes.ApplyTheme(client, theme.Id);
            config.Settings.Theme = theme.Id;
            config.SaveSettings();
            client.Toast($"Theme set to {theme.Name}", "Saved");
        }, selectedTheme);

        var themeOptions = TendrilThemes.All
            .Select(t => new Option<string>(t.Name, t.Id))
            .ToArray<IAnyOption>();

        var activeTheme = TendrilThemes.GetTheme(selectedTheme.Value);
        var swatches = Layout.Horizontal()
            | activeTheme.PreviewColors.Select(color =>
                new Svg($"<svg width='20' height='20' viewBox='0 0 20 20'><circle cx='10' cy='10' r='9' fill='{color}' stroke='rgba(128,128,128,0.3)' stroke-width='1.5'/></svg>")
                    .Width(Size.Px(20))
                    .Height(Size.Px(20))
            ).ToArray();

        var themeSelector = Layout.Vertical()
            | selectedTheme.ToSelectInput(themeOptions)
            | swatches;

        var isSidebarOpen = config.Settings.SidebarOpen;

        return Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Appearance").Bold()
               | Text.Muted("Choose how Tendril appears. System matches your OS setting.").Small()
               | (Layout.Horizontal()
                  | new Button("Light")
                      .Variant(themeMode.Value.Equals("light", StringComparison.OrdinalIgnoreCase) ? ButtonVariant.Primary : ButtonVariant.Outline)
                      .Icon(Icons.Sun)
                      .OnClick(() =>
                      {
                          themeMode.Set("light");
                          client.SetThemeMode(ThemeMode.Light);
                          config.Settings.ThemeMode = "light";
                          config.SaveSettings();
                          client.Toast("Appearance set to Light", "Saved");
                      })
                  | new Button("Dark")
                      .Variant(themeMode.Value.Equals("dark", StringComparison.OrdinalIgnoreCase) ? ButtonVariant.Primary : ButtonVariant.Outline)
                      .Icon(Icons.Moon)
                      .OnClick(() =>
                      {
                          themeMode.Set("dark");
                          client.SetThemeMode(ThemeMode.Dark);
                          config.Settings.ThemeMode = "dark";
                          config.SaveSettings();
                          client.Toast("Appearance set to Dark", "Saved");
                      })
                  | new Button("System")
                      .Variant(themeMode.Value.Equals("system", StringComparison.OrdinalIgnoreCase) ? ButtonVariant.Primary : ButtonVariant.Outline)
                      .Icon(Icons.SunMoon)
                      .OnClick(() =>
                      {
                          themeMode.Set("system");
                          client.SetThemeMode(ThemeMode.System);
                          config.Settings.ThemeMode = "system";
                          config.SaveSettings();
                          client.Toast("Appearance set to System", "Saved");
                      }))
               | Text.Block("Theme").Bold()
               | Text.Muted("Choose a color scheme preset for Tendril.").Small()
               | themeSelector
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
