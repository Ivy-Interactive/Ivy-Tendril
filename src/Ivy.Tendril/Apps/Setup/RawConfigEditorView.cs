using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Setup;

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

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(200)))
               | Text.Block("config.yaml").Bold()
               | Text.Muted(config.ConfigPath).Small()
               | (errorMessage.Value != null
                   ? Text.Block(errorMessage.Value!).Color(Colors.Destructive)
                   : null!)
               | yamlText.ToTextareaInput()
                   .Rows(30)
                   .Width(Size.Grow())
               | (Layout.Horizontal().Gap(2)
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
