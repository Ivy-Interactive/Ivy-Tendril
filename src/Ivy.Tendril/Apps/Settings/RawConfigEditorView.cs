using System.Reactive.Disposables;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Settings;

public class RawConfigEditorView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();

        // loadedYaml tracks the content last loaded from disk (mount, Save, Reload, or an
        // external-reload pickup below), so hasChanges reflects edits the user made since then
        // rather than a snapshot re-read from disk on every Build().
        var loadedYaml = UseState(LoadYaml(config));
        var yamlText = UseState(loadedYaml.Value);
        var errorMessage = UseState<string?>(null);

        var hasChanges = yamlText.Value != loadedYaml.Value;

        // Only pull in an externally-reloaded config.yaml when the user has no unsaved edits —
        // otherwise an external CLI write would silently clobber text they're mid-edit on.
        UseEffect(() =>
        {
            void OnSettingsReloaded(object? sender, EventArgs e)
            {
                if (yamlText.Value != loadedYaml.Value) return;
                var fresh = LoadYaml(config);
                loadedYaml.Set(fresh);
                yamlText.Set(fresh);
                errorMessage.Set(null);
            }
            config.SettingsReloaded += OnSettingsReloaded;
            return Disposable.Create(() => config.SettingsReloaded -= OnSettingsReloaded);
        });

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
                              loadedYaml.Set(yamlText.Value ?? "");
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
                          var fresh = LoadYaml(config);
                          loadedYaml.Set(fresh);
                          yamlText.Set(fresh);
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
