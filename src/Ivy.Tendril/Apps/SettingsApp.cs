using Ivy.Desktop;
using Ivy.Tendril.Apps.Setup;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Apps;

[App(title: "Configuration", icon: Icons.Settings, isVisible: false)]
public class SettingsApp : ViewBase
{
    private const string TagGeneral = "general";
    private const string TagNotifications = "notifications";
    private const string TagSecurity = "security";
    private const string TagLevels = "levels";
    private const string TagVerifications = "verifications";
    private const string TagPromptwares = "promptwares";
    private const string TagProjects = "projects";
    private const string TagAdvanced = "advanced";
    private const string TagOpenConfig = "open-config";

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var navigator = UseNavigation();
        var client = UseService<IClientProvider>();
        var httpContextAccessor = UseService<IHttpContextAccessor>();
        var selected = UseState(TagGeneral);
        Context.TryUseService<DesktopWindow>(out var desktopWindow);
        var isDesktop = desktopWindow != null;
        var capturedHost = ConfigYamlUiHelper.CaptureHost(httpContextAccessor);

        var menuItems = new[]
        {
            MenuItem.Default("Configuration")
                .Icon(Icons.Settings2)
                .Expanded()
                .Children(
                    MenuItem.Default("General", TagGeneral).Icon(Icons.Settings),
                    MenuItem.Default("Notifications", TagNotifications).Icon(Icons.Bell),
                    MenuItem.Default("Security", TagSecurity).Icon(Icons.Lock),
                    MenuItem.Default("Levels", TagLevels).Icon(Icons.ListOrdered),
                    MenuItem.Default("Verifications", TagVerifications).Icon(Icons.CircleCheck),
                    MenuItem.Default("Promptwares", TagPromptwares).Icon(Icons.Wand),
                    MenuItem.Default("Projects", TagProjects).Icon(Icons.Folder),
                    MenuItem.Default("Advanced", TagAdvanced).Icon(Icons.Cog),
                    MenuItem.Default("Open config.yaml", TagOpenConfig).Icon(Icons.FileText)
                )
        };

        void OnSelect(Event<SidebarMenu, object> @event)
        {
            if (@event.Value is not string tag) return;
            switch (tag)
            {
                case TagOpenConfig:
                    ConfigYamlUiHelper.OpenOrNavigate(config, navigator, client, isDesktop, capturedHost);
                    break;
                default:
                    selected.Set(tag);
                    break;
            }
        }

        var sidebar = new SidebarMenu(OnSelect, menuItems);

        object content = selected.Value switch
        {
            TagGeneral => new GeneralSetupView(),
            TagNotifications => new NotificationsSetupView(),
            TagSecurity => new SecuritySetupView(),
            TagLevels => new LevelsSetupView(),
            TagVerifications => new VerificationsSetupView(),
            TagPromptwares => new PromptwaresSetupView(),
            TagProjects => new ProjectsSetupView(),
            TagAdvanced => new AdvancedSetupView(),
            _ => new GeneralSetupView()
        };

        return new SidebarLayout(content, sidebar);
    }
}
