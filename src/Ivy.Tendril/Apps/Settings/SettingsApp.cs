using Ivy.Desktop;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Apps.Settings;

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
    internal const string TagProjects = "projects";
    private const string TagTunnel = "tunnel";
    private const string TagSlackbot = "slackbot";
    private const string TagAdvanced = "advanced";

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var navigator = UseNavigation();
        var client = UseService<IClientProvider>();
        var httpContextAccessor = UseService<IHttpContextAccessor>();
        var args = UseArgs<SettingsAppArgs>();
        var selected = UseState(() => args?.Section ?? TagCodingAgent);
        Context.TryUseService<DesktopWindow>(out var desktopWindow);
        var isDesktop = desktopWindow != null;
        var capturedHost = ConfigYamlUiHelper.CaptureHost(httpContextAccessor);

        var sections = new List<(string Label, string Tag, Icons Icon)>
        {
            ("Coding Agent", TagCodingAgent, Icons.Bot),
            ("Plans", TagPlans, Icons.Feather),
            ("Appearance", TagAppearance, Icons.Sun),
            ("Projects", TagProjects, Icons.Folder),
            ("Verifications", TagVerifications, Icons.CircleCheck),
            ("Promptwares", TagPromptwares, Icons.Wand),
            ("Levels", TagLevels, Icons.ListOrdered),
            ("Notifications", TagNotifications, Icons.Bell),
            ("Security", TagSecurity, Icons.Lock),
            ("Tunnel", TagTunnel, Icons.Globe),
            ("Slackbot", TagSlackbot, Icons.Slack),
            ("Advanced", TagAdvanced, Icons.Cog),
        };

        var rows = sections
            .Select(s => SidebarListRow.Build(s.Label, s.Icon, () => selected.Set(s.Tag), selected.Value == s.Tag))
            .Append(SidebarListRow.Build("Open config.yaml", Icons.FileText,
                () => ConfigYamlUiHelper.OpenOrNavigate(config, navigator, client, isDesktop, capturedHost)));

        var sidebarHeader = Layout.Horizontal().Gap(2).AlignContent(Align.Left).Height(Size.Px(40))
            | Icons.Settings2.ToIcon()
            | Text.Literal("Configuration");

        var sidebar = new HeaderLayout(sidebarHeader, Layout.Vertical(rows).Gap(1));

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
            TagTunnel => new TunnelSetupView(),
            TagSlackbot => new SlackbotSetupView(),
            TagAdvanced => new AdvancedSetupView(),
            _ => new CodingAgentSetupView()
        };

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
