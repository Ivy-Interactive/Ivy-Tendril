using System.Reactive.Disposables;
using Ivy.Core.Hooks;
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
    private const string TagAdvanced = "advanced";

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var navigator = UseNavigation();
        var client = UseService<IClientProvider>();
        var httpContextAccessor = UseService<IHttpContextAccessor>();
        var refreshToken = UseRefreshToken();
        var args = UseArgs<SettingsAppArgs>();
        var selected = UseState(() => args?.Section ?? TagCodingAgent);
        var isProjectsExpanded = UseState(false);

        var (addProjectDialog, openAddProjectDialog) = UseTrigger((IState<bool> isOpen) =>
            new AddProjectDialog(isOpen, config, client, refreshToken, onCreated: newProjName =>
            {
                var projectsList = config.Settings.Projects;
                var newIdx = projectsList.FindIndex(p => p.Name.Equals(newProjName, StringComparison.OrdinalIgnoreCase));
                if (newIdx >= 0) selected.Set($"project:{newIdx}");
            }));

        Context.TryUseService<DesktopWindow>(out var desktopWindow);

        UseEffect(() =>
        {
            void OnSettingsReloaded(object? sender, EventArgs e) => refreshToken.Refresh();
            config.SettingsReloaded += OnSettingsReloaded;
            return Disposable.Create(() => config.SettingsReloaded -= OnSettingsReloaded);
        });

        _ = refreshToken.Token;
        var isDesktop = desktopWindow != null;
        var capturedHost = ConfigYamlUiHelper.CaptureHost(httpContextAccessor);

        var projects = config.Settings.Projects;
        var selectedTag = selected.Value;

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
            ("Advanced", TagAdvanced, Icons.Cog),
        };

        var rows = new List<object>
        {
            SidebarListRow.Build("Coding Agent", Icons.Bot, () => selected.Set(TagCodingAgent), selectedTag == TagCodingAgent),
            SidebarListRow.Build("Plans", Icons.Feather, () => selected.Set(TagPlans), selectedTag == TagPlans),
            SidebarListRow.Build("Appearance", Icons.Sun, () => selected.Set(TagAppearance), selectedTag == TagAppearance),

            SidebarListRow.Build(
                "Projects",
                isProjectsExpanded.Value ? Icons.ChevronDown : Icons.ChevronRight,
                () =>
                {
                    var willExpand = !isProjectsExpanded.Value;
                    isProjectsExpanded.Set(willExpand);
                    if (willExpand && selectedTag != TagProjects && !selectedTag.StartsWith("project:"))
                    {
                        if (projects.Count > 0) selected.Set("project:0");
                    }
                },
                selectedTag == TagProjects
            )
        };

        if (isProjectsExpanded.Value)
        {
            for (int i = 0; i < projects.Count; i++)
            {
                var proj = projects[i];
                var tag = $"project:{i}";
                var isSelected = selectedTag == tag || (selectedTag == TagProjects && i == 0);
                var localIdx = i;
                var projColor = Enum.TryParse<Colors>(proj.Color, out var parsed) ? parsed : (config.GetProjectColor(proj.Name) ?? Colors.Slate);
                rows.Add(SidebarListRow.BuildSubItem(proj.Name, null, projColor, () => selected.Set($"project:{localIdx}"), isSelected));
            }
            rows.Add(SidebarListRow.BuildSubItem("Add Project", Icons.Plus, () => openAddProjectDialog(), false));
        }

        rows.Add(SidebarListRow.Build("Verifications", Icons.CircleCheck, () => selected.Set(TagVerifications), selectedTag == TagVerifications));
        rows.Add(SidebarListRow.Build("Promptwares", Icons.Wand, () => selected.Set(TagPromptwares), selectedTag == TagPromptwares));
        rows.Add(SidebarListRow.Build("Levels", Icons.ListOrdered, () => selected.Set(TagLevels), selectedTag == TagLevels));
        rows.Add(SidebarListRow.Build("Notifications", Icons.Bell, () => selected.Set(TagNotifications), selectedTag == TagNotifications));
        rows.Add(SidebarListRow.Build("Security", Icons.Lock, () => selected.Set(TagSecurity), selectedTag == TagSecurity));
        rows.Add(SidebarListRow.Build("Tunnel", Icons.Globe, () => selected.Set(TagTunnel), selectedTag == TagTunnel));
        rows.Add(SidebarListRow.Build("Advanced", Icons.Cog, () => selected.Set(TagAdvanced), selectedTag == TagAdvanced));
        rows.Add(SidebarListRow.Build("Open config.yaml", Icons.FileText, () => ConfigYamlUiHelper.OpenOrNavigate(config, navigator, client, isDesktop, capturedHost), false));

        var sidebar = Layout.Vertical(rows).Gap(1);

        object content;
        if (selectedTag.StartsWith("project:"))
        {
            var idxStr = selectedTag["project:".Length..];
            if (int.TryParse(idxStr, out var projIdx) && projIdx >= 0 && projIdx < projects.Count)
            {
                content = new ProjectDetailView(
                    projIdx,
                    projects,
                    config,
                    client,
                    refreshToken,
                    onDeleteProject: () =>
                    {
                        var name = projects[projIdx].Name;
                        projects.RemoveAt(projIdx);
                        config.SaveSettings();
                        if (projects.Count > 0) selected.Set("project:0");
                        else selected.Set(TagCodingAgent);
                        refreshToken.Refresh();
                        client.Toast($"Deleted project '{name}'", "Deleted");
                    }).Key($"project:{projects[projIdx].Name}");
            }
            else
            {
                content = projects.Count > 0
                    ? new ProjectDetailView(0, projects, config, client, refreshToken).Key($"project:{projects[0].Name}")
                    : new CodingAgentSetupView();
            }
        }
        else
        {
            content = selectedTag switch
            {
                TagCodingAgent => new CodingAgentSetupView(),
                TagPlans => new PlansSetupView(),
                TagAppearance => new AppearanceSetupView(),
                TagNotifications => new NotificationsSetupView(),
                TagSecurity => new SecuritySetupView(),
                TagLevels => new LevelsSetupView(),
                TagVerifications => new VerificationsSetupView(),
                TagPromptwares => new PromptwaresSetupView(),
                TagProjects => projects.Count > 0
                    ? new ProjectDetailView(0, projects, config, client, refreshToken).Key($"project:{projects[0].Name}")
                    : new CodingAgentSetupView(),
                TagTunnel => new TunnelSetupView(),
                TagAdvanced => new AdvancedSetupView(),
                _ => new CodingAgentSetupView()
            };
        }

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
                                      | (Layout.Vertical().Height(Size.Grow()) | content)
                                      | addProjectDialog;

        return new SidebarLayout(contentWithMobileHeader, sidebar);
    }
}
