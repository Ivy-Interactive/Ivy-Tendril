using Ivy.Tendril.Apps.Setup;

namespace Ivy.Tendril.Apps;

public record SetupAppArgs(int SelectedTab = 0);

[App(title: "Setup", icon: Icons.Construction, isVisible: false)]
public class SetupApp : ViewBase
{
    public override object Build()
    {
        var appArgs = UseArgs<SetupAppArgs>();
        var selectedTab = UseState(appArgs?.SelectedTab ?? 0);

        return Layout.Tabs(
            new Tab("General", new GeneralSetupView()),
            new Tab("Security", new SecuritySetupView()),
            new Tab("Levels", new LevelsSetupView()),
            new Tab("Verifications", new VerificationsSetupView()),
            new Tab("Promptwares", new PromptwaresSetupView()),
            new Tab("Projects", new ProjectsSetupView()),
            new Tab("Advanced", new AdvancedSetupView()),
            new Tab("Config", new RawConfigEditorView())
        ).OnSelect(v => selectedTab.Set(v)).SelectedIndex(selectedTab.Value).Variant(TabsVariant.Content);
    }
}
