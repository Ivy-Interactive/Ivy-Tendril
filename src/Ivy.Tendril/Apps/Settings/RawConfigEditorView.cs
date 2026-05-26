using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Settings;

public class RawConfigEditorView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();

        var yamlText = UseState(LoadYaml(config));
        var errorMessage = UseState<string?>(null);

        var originalYaml = LoadYaml(config);
        var hasChanges = yamlText.Value != originalYaml;

        // CodeInput fills height reliably in web layout; plain textarea ignores flex grow.
        // Button row: never use AlignContent(Align.Right) on Horizontal — that aligns on the
        // cross axis (vertical) and pushes controls to the bottom of a tall row.
        return Layout.Vertical().Gap(2).Padding(2).Height(Size.Full()).Width(Size.Full())
               .RemoveParentPadding()
               | Text.Muted(config.ConfigPath).Small()
               | (errorMessage.Value != null
                   ? Text.Block(errorMessage.Value!).Color(Colors.Destructive)
                   : null!)
               | (Layout.Vertical())
                  | yamlText.ToCodeInput(language: Languages.Yaml)
                      .Height(Size.Full())
                      .Width(Size.Full())
               | (Layout.Horizontal().Gap(2).Height(Size.Fit())
                  | new Button("Save").Primary()
                      .Disabled(!hasChanges)
                      .OnClick(() =>
                      {
                          errorMessage.Set(null);
                          try
                          {
                              FileHelper.WriteAllText(config.ConfigPath, yamlText.Value ?? "");
                              config.ReloadSettings();
                              client.Toast("config.yaml saved and reloaded", "Saved");
                          }
                          catch (Exception ex)
                          {
                              errorMessage.Set($"Save failed: {ex.Message}");
                          }
                      })
                  | new Button("Reload from disk").Outline()
                      .OnClick(() =>
                      {
                          yamlText.Set(LoadYaml(config));
                          errorMessage.Set(null);
                      }));
    }

    private static string LoadYaml(IConfigService config)
    {
        try
        {
            return File.Exists(config.ConfigPath)
                ? File.ReadAllText(config.ConfigPath)
                : "";
        }
        catch
        {
            return "";
        }
    }
}
