using Ivy.Tendril.AppShell.Dialogs;
using Ivy.Tendril.Apps.Setup;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

[App(title: "Settings", icon: Icons.Settings, isVisible: false)]
public class SettingsApp : ViewBase
{
    private const string TagGeneral = "general";
    private const string TagSecurity = "security";
    private const string TagLevels = "levels";
    private const string TagVerifications = "verifications";
    private const string TagPromptwares = "promptwares";
    private const string TagProjects = "projects";
    private const string TagAdvanced = "advanced";
    private const string TagTheme = "theme";
    private const string TagTrash = "trash";
    private const string TagImportIssues = "import-issues";
    private const string TagOpenConfig = "open-config";

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var selected = UseState(TagGeneral);
        var importIssuesDialogOpen = UseState(false);

        var menuItems = new[]
        {
            MenuItem.Default("Configuration")
                .Icon(Icons.Settings2)
                .Expanded()
                .Children(
                    MenuItem.Default("General", TagGeneral).Icon(Icons.Settings),
                    MenuItem.Default("Security", TagSecurity).Icon(Icons.Lock),
                    MenuItem.Default("Levels", TagLevels).Icon(Icons.ListOrdered),
                    MenuItem.Default("Verifications", TagVerifications).Icon(Icons.CircleCheck),
                    MenuItem.Default("Promptwares", TagPromptwares).Icon(Icons.Wand),
                    MenuItem.Default("Projects", TagProjects).Icon(Icons.Folder),
                    MenuItem.Default("Advanced", TagAdvanced).Icon(Icons.Cog)
                ),
            MenuItem.Default("Tools")
                .Icon(Icons.Wrench)
                .Expanded()
                .Children(
                    MenuItem.Default("Theme", TagTheme).Icon(Icons.SunMoon),
                    MenuItem.Default("Trash", TagTrash).Icon(Icons.Trash2),
                    MenuItem.Default("Import Issues from GitHub", TagImportIssues).Icon(Icons.Download),
                    MenuItem.Default("Open config.yaml", TagOpenConfig).Icon(Icons.FileText)
                )
        };

        void OnSelect(Event<SidebarMenu, object> @event)
        {
            if (@event.Value is not string tag) return;
            switch (tag)
            {
                case TagImportIssues:
                    importIssuesDialogOpen.Set(true);
                    break;
                case TagOpenConfig:
                    config.OpenInEditor(config.ConfigPath);
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
            TagSecurity => new SecuritySetupView(),
            TagLevels => new LevelsSetupView(),
            TagVerifications => new VerificationsSetupView(),
            TagPromptwares => new PromptwaresSetupView(),
            TagProjects => new ProjectsSetupView(),
            TagAdvanced => new AdvancedSetupView(),
            TagTheme => new ThemeSettingsView(),
            TagTrash => new TrashApp(),
            _ => new GeneralSetupView()
        };

        return new Fragment(
            new SidebarLayout(content, sidebar),
            new ImportIssuesDialog(importIssuesDialogOpen, config)
        );
    }
}
