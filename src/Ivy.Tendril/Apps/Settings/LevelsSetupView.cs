using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Settings;

public class LevelsSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();
        var (triggerView, showTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new EditLevelDialogContent(isOpen, existingIndex, config, client, refreshToken));
        var (alertView, showAlert) = UseAlert();

        // Use levels in config.yaml order (not alphabetically sorted).
        var levels = config.Settings.Levels;

        var rows = levels.Select((level, i) => new LevelRow(level.Name, level.Color, i)).ToList();

        var table = new TableBuilder<LevelRow>(rows)
            .Builder(t => t.Color, f => f.Func<LevelRow, string>(color =>
                new Badge(color).Color(
                    Enum.TryParse<Colors>(color, out var c) ? c : Colors.Gray
                )
            ))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<LevelRow, int>(idx =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit this level").OnClick(() =>
                {
                    showTrigger(idx);
                })
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete this level").OnClick(() =>
                {
                    var name = levels[idx].Name;
                    showAlert($"Are you sure you want to delete '{name}'?", result =>
                    {
                        if (result == AlertResult.Ok)
                        {
                            var removed = levels[idx];
                            levels.RemoveAt(idx);
                            try
                            {
                                config.SaveSettings();
                                client.Toast($"Level '{name}' deleted", "Deleted");
                                refreshToken.Refresh();
                            }
                            catch (Exception ex)
                            {
                                levels.Insert(idx, removed);
                                refreshToken.Refresh();
                                client.Toast($"Failed to delete level: {ex.Message}", "Error");
                            }
                        }
                    }, "Delete Level");
                })
            ))
            .Width(Size.Fit());

        return Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(200)))
               | Text.Block("Priority Levels").Bold()
               | Text.Block("Define priority levels used to categorize plans.").Muted().Small()
               | table
               | new Button("Add Level").Icon(Icons.Plus).Outline().OnClick(() =>
               {
                   showTrigger(null);
               })
               | triggerView
               | alertView;
    }

    private record LevelRow(string Name, string Color, int Index);
}

file class EditLevelDialogContent(
    IState<bool> isOpen,
    int? existingIndex,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editColor = UseState("Gray");
        UseEffect(() =>
        {
            var levels = config.Settings.Levels;
            if (existingIndex != null && existingIndex >= 0 && existingIndex < levels.Count)
            {
                editName.Set(levels[existingIndex.Value].Name);
                editColor.Set(levels[existingIndex.Value].Color);
            }
        }, EffectTrigger.OnMount());

        var levels = config.Settings.Levels;
        var colorOptions = Enum.GetNames<Colors>().ToList();
        var isNew = existingIndex == null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(isNew ? "Add Level" : "Edit Level"),
            new DialogBody(
                Layout.Vertical()
                | editName.ToTextInput("Level name...").WithField().Label("Name")
                | editColor.ToSelectInput(colorOptions).WithField().Label("Color")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    var oldLevelName = isNew ? null : levels[existingIndex!.Value].Name;
                    var oldColor = isNew ? null : levels[existingIndex!.Value].Color;
                    if (isNew)
                    {
                        levels.Add(new LevelConfig { Name = editName.Value, Color = editColor.Value });
                    }
                    else
                    {
                        var level = levels[existingIndex!.Value];
                        level.Name = editName.Value;
                        level.Color = editColor.Value;
                    }

                    try
                    {
                        config.SaveSettings();
                        isOpen.Set(false);
                        refreshToken.Refresh();
                        client.Toast("Level saved", "Saved");
                    }
                    catch (Exception ex)
                    {
                        if (isNew)
                            levels.RemoveAt(levels.Count - 1);
                        else
                        {
                            var level = levels[existingIndex!.Value];
                            level.Name = oldLevelName!;
                            level.Color = oldColor!;
                        }
                        refreshToken.Refresh();
                        client.Toast($"Failed to save level: {ex.Message}", "Error");
                    }
                })
            )
        ).Width(Size.Rem(25));
    }
}
