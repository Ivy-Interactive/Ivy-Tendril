using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class EditorSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();

        var editorCommand = UseState(config.Settings.Editor.Command);
        var editorLabel = UseState(config.Settings.Editor.Label);

        var hasChanges = editorCommand.Value != config.Settings.Editor.Command
                         || editorLabel.Value != config.Settings.Editor.Label;

        return Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("External Editor").Bold()
               | Text.Muted("Configure the command used to open configuration files and workspace directories locally.").Small()
               | editorCommand.ToTextInput("e.g. code, vim")
                   .WithField().Label("Command")
                   .Description(!config.Editor.IsAvailable
                       ? $"⚠ '{config.Editor.Command}' was not found in PATH"
                       : null)
               | editorLabel.ToTextInput("e.g. VS Code, Vim")
                   .WithField().Label("Label")
               | new Button("Save Editor Preferences").Primary()
                   .Disabled(!hasChanges)
                   .OnClick(() =>
                   {
                       config.Settings.Editor.Command = editorCommand.Value;
                       config.Settings.Editor.Label = editorLabel.Value;
                       config.SaveSettings();
                       client.Toast("Editor preferences saved and applied", "Saved");
                   });
    }
}
