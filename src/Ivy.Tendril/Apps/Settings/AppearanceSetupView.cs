using Ivy.Tendril.Services;
using Ivy.Tendril.Themes;

namespace Ivy.Tendril.Apps.Settings;

public class AppearanceSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();

        var isSidebarOpen = config.Settings.SidebarOpen;
        var currentThemeId = config.Settings.Theme ?? "default";

        var themeGrid = Layout.Grid()
            .Columns(1.At(Breakpoint.Mobile).And(Breakpoint.Tablet, 2).And(Breakpoint.Desktop, 2))
            .Gap(3);

        themeGrid = TendrilThemes.All.Aggregate(themeGrid, (current, theme) =>
        {
            var isSelected = theme.Id.Equals(currentThemeId, StringComparison.OrdinalIgnoreCase);

            var swatches = Layout.Horizontal().Gap(1)
                | theme.PreviewColors.Select(color =>
                    new Svg($"<svg width='20' height='20' viewBox='0 0 20 20'><circle cx='10' cy='10' r='9' fill='{color}' stroke='rgba(128,128,128,0.3)' stroke-width='1.5'/></svg>")
                        .Width(Size.Px(20))
                        .Height(Size.Px(20))
                ).ToArray();

            var cardContent = Layout.Vertical().Gap(2)
                | (Layout.Horizontal().AlignContent(Align.Center)
                    | Text.Block(theme.Name).Bold()
                    | new Badge(theme.IsDark ? "Dark" : "Light", theme.IsDark ? BadgeVariant.Secondary : BadgeVariant.Outline).Small()
                    | new Spacer()
                    | (isSelected ? new Badge("Active", BadgeVariant.Success).Small().Icon(Icons.Check) : null))
                | Text.Muted(theme.Description).Small()
                | swatches;

            return current | new Card(cardContent)
                .Width(Size.Full())
                .OnClick(() =>
                {
                    TendrilThemes.ApplyTheme(client, theme.Id);
                    config.Settings.Theme = theme.Id;
                    config.SaveSettings();
                    client.Toast($"Theme set to {theme.Name}", "Saved");
                });
        });

        return Layout.Vertical().Width(Size.Auto().Max(Size.Units(120))).Gap(4)
               | Text.Block("Appearance").Bold()
               | Text.Muted("Choose how Tendril appears. System matches your OS setting.").Small()
               | (Layout.Horizontal()
                  | new Button("Light").Variant(ButtonVariant.Outline).Icon(Icons.Sun)
                      .OnClick(() => client.SetThemeMode(ThemeMode.Light))
                  | new Button("Dark").Variant(ButtonVariant.Outline).Icon(Icons.Moon)
                      .OnClick(() => client.SetThemeMode(ThemeMode.Dark))
                  | new Button("System").Variant(ButtonVariant.Outline).Icon(Icons.SunMoon)
                      .OnClick(() => client.SetThemeMode(ThemeMode.System)))
               | new Separator()
               | Text.Block("Theme").Bold()
               | Text.Muted("Choose a DaisyUI-inspired color scheme preset for Tendril.").Small()
               | themeGrid
               | new Separator()
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
