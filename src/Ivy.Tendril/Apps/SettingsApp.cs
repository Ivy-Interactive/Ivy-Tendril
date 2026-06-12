using Ivy.Desktop;
using Ivy.Tendril.Apps.Settings;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Apps;

[App(title: "Configuration", icon: Icons.Settings, isVisible: false)]
public class SettingsApp : ViewBase
{
    private const string TagCodingAgent = "coding-agent";
    private const string TagPlans = "plans";
    private const string TagAppearance = "appearance";
    private const string TagNotifications = "notifications";
    private const string TagSecurity = "security";
    private const string TagLevels = "levels";
    private const string TagVerifications = "verifications";
    private const string TagPromptwares = "promptwares";
    private const string TagProjects = "projects";
    private const string TagPlugins = "plugins";
    private const string TagTunnel = "tunnel";
    private const string TagAdvanced = "advanced";
    private const string TagOpenConfig = "open-config";

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var tendrilArgs = UseService<TendrilArgs>();
        var navigator = UseNavigation();
        var client = UseService<IClientProvider>();
        var httpContextAccessor = UseService<IHttpContextAccessor>();
        var selected = UseState(TagCodingAgent);
        Context.TryUseService<DesktopWindow>(out var desktopWindow);
        var isDesktop = desktopWindow != null;
        var capturedHost = ConfigYamlUiHelper.CaptureHost(httpContextAccessor);

        var children = new List<MenuItem>
        {
            MenuItem.Default("Coding Agent", TagCodingAgent).Icon(Icons.Bot),
            MenuItem.Default("Plans", TagPlans).Icon(Icons.Feather),
            MenuItem.Default("Appearance", TagAppearance).Icon(Icons.Sun),
            MenuItem.Default("Projects", TagProjects).Icon(Icons.Folder),
            MenuItem.Default("Verifications", TagVerifications).Icon(Icons.CircleCheck),
            MenuItem.Default("Promptwares", TagPromptwares).Icon(Icons.Wand),
            MenuItem.Default("Levels", TagLevels).Icon(Icons.ListOrdered),
            MenuItem.Default("Notifications", TagNotifications).Icon(Icons.Bell),
            MenuItem.Default("Security", TagSecurity).Icon(Icons.Lock),
            MenuItem.Default("Plugins", TagPlugins).Icon(Icons.Plug),
        };

        if (tendrilArgs.Beta)
            children.Add(MenuItem.Default("Tunnel", TagTunnel).Icon(Icons.Globe));

        children.Add(MenuItem.Default("Advanced", TagAdvanced).Icon(Icons.Cog));
        children.Add(MenuItem.Default("Open config.yaml", TagOpenConfig).Icon(Icons.FileText));

        var menuItems = new[]
        {
            MenuItem.Default("Configuration")
                .Icon(Icons.Settings2)
                .Expanded()
                .Children(children.ToArray())
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
            TagCodingAgent => new CodingAgentSetupView(),
            TagPlans => new PlansSetupView(),
            TagAppearance => new AppearanceSetupView(),
            TagNotifications => new NotificationsSetupView(),
            TagSecurity => new SecuritySetupView(),
            TagLevels => new LevelsSetupView(),
            TagVerifications => new VerificationsSetupView(),
            TagPromptwares => new PromptwaresSetupView(),
            TagProjects => new ProjectsSetupView(),
            TagPlugins => new PluginsSetupView(),
            TagTunnel => new TunnelSetupView(),
            TagAdvanced => new AdvancedSetupView(),
            _ => new CodingAgentSetupView()
        };

        var sections = children
            .Where(m => m.Tag is string t && t != TagOpenConfig)
            .Select(m => (Tag: (string)m.Tag!, Label: m.Label ?? ""))
            .ToList();
        var currentLabel = sections.FirstOrDefault(s => s.Tag == selected.Value).Label ?? "Configuration";

        var mobileHeader = MobileItemPicker.Build(
                currentLabel,
                sections,
                s => s.Label,
                s => s.Tag == selected.Value,
                s => selected.Set(s.Tag))
            .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);

        var contentWithMobileHeader = Layout.Vertical().Height(Size.Full()).Gap(2)
                                      | mobileHeader
                                      | (Layout.Vertical().Height(Size.Grow()) | content);

        return new SidebarLayout(contentWithMobileHeader, sidebar);
    }
}
