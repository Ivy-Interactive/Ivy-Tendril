using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Setup;

public class PromptwaresSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();
        var (triggerView, showTrigger) = UseTrigger((IState<bool> isOpen, string? existingKey) =>
            new EditPromptwareDialogContent(isOpen, existingKey, config, client, refreshToken));
        var (alertView, showAlert) = UseAlert();

        var promptwares = config.Settings.Promptwares;

        var rows = promptwares.Select((kvp, i) => new PromptwareRow(
            kvp.Key,
            kvp.Value.Profile,
            i
        )).ToList();

        var table = new TableBuilder<PromptwareRow>(rows)
            .Builder(t => t.Name, f => f.Func<PromptwareRow, string>(name =>
                Constants.JobTypes.BuiltIn.Contains(name)
                    ? Layout.Horizontal().Gap(2) | name | new Badge("Tendril").Variant(BadgeVariant.Secondary).Small()
                    : name))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<PromptwareRow, int>(idx =>
            {
                var name = rows[idx].Name;
                var isBuiltIn = Constants.JobTypes.BuiltIn.Contains(name);
                return Layout.Horizontal().Gap(1)
                       | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() =>
                       {
                           showTrigger(name);
                       })
                       | (isBuiltIn
                           ? null
                           : new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                           {
                               showAlert($"Are you sure you want to delete '{name}'?", result =>
                               {
                                   if (result != AlertResult.Ok) return;
                                   var removedValue = promptwares[name];
                                   promptwares.Remove(name);
                                   try
                                   {
                                       config.SaveSettings();
                                       client.Toast($"Promptware '{name}' deleted", "Deleted");
                                       refreshToken.Refresh();
                                   }
                                   catch (Exception ex)
                                   {
                                       promptwares[name] = removedValue;
                                       refreshToken.Refresh();
                                       client.Toast($"Failed to delete promptware: {ex.Message}", "Error");
                                   }
                               }, "Delete Promptware");
                           }));
            }))
            .ColumnWidth(t => t.Index, Size.Units(8))
            .ColumnWidth(t => t.Name, Size.Units(50))
            .ColumnWidth(t => t.Profile, Size.Units(20))
            .Width(Size.Full());

        return Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(200)))
               | Text.Block("Promptware Configuration").Bold()
               | Text.Block("Configure agent profile and tool permissions for each promptware.")
                   .Muted().Small()
               | table
               | new Button("Add Promptware").Icon(Icons.Plus).Outline().OnClick(() =>
               {
                   showTrigger(null);
               })
               | triggerView
               | alertView;
    }

    private record PromptwareRow(string Name, string Profile, int Index);
}

file class EditPromptwareDialogContent(
    IState<bool> isOpen,
    string? existingKey,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editProfile = UseState("");
        var editAllowedTools = UseState("");
        var editCustomInstructions = UseState("");
        UseEffect(() =>
        {
            var pw = config.Settings.Promptwares;
            if (existingKey != null && pw.ContainsKey(existingKey))
            {
                var p = pw[existingKey];
                editName.Set(existingKey);
                editProfile.Set(p.Profile);
                editAllowedTools.Set(string.Join("\n", p.AllowedTools));
                editCustomInstructions.Set(p.CustomInstructions ?? "");
            }
        }, EffectTrigger.OnMount());

        var promptwares = config.Settings.Promptwares;
        var isNew = existingKey == null;

        var profileOptions = config.Settings.CodingAgents
            .SelectMany(a => a.Profiles)
            .Select(p => p.Name)
            .Distinct()
            .Select(name => new Option<string>(char.ToUpper(name[0]) + name[1..], name))
            .ToArray();

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(isNew ? "Add Promptware" : "Edit Promptware"),
            new DialogBody(
                Layout.Vertical()
                | editName.ToTextInput("Promptware name (e.g. CreatePlan)...").WithField().Label("Name")
                | editProfile.ToSelectInput(profileOptions).Variant(SelectInputVariant.Toggle).WithField().Label("Profile")
                | editAllowedTools.ToCodeInput().WithField().Label("Allowed Tools")
                | editCustomInstructions.ToTextInput().Multiline().WithField().Label("Custom Instructions")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;

                    var tools = editAllowedTools.Value
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .ToList();

                    var pwConfig = new PromptwareConfig
                    {
                        Profile = editProfile.Value,
                        AllowedTools = tools,
                        CustomInstructions = string.IsNullOrWhiteSpace(editCustomInstructions.Value)
                            ? null
                            : editCustomInstructions.Value
                    };

                    var wasRenamed = !isNew && existingKey != editName.Value;
                    PromptwareConfig? oldValue = null;
                    if (wasRenamed)
                    {
                        oldValue = promptwares[existingKey!];
                        promptwares.Remove(existingKey!);
                    }
                    else if (!isNew)
                    {
                        oldValue = promptwares[existingKey!];
                    }

                    promptwares[editName.Value] = pwConfig;
                    try
                    {
                        config.SaveSettings();
                        isOpen.Set(false);
                        refreshToken.Refresh();
                        client.Toast("Promptware saved", "Saved");
                    }
                    catch (Exception ex)
                    {
                        promptwares.Remove(editName.Value);
                        if (wasRenamed)
                            promptwares[existingKey!] = oldValue!;
                        else if (!isNew)
                            promptwares[existingKey!] = oldValue!;
                        refreshToken.Refresh();
                        client.Toast($"Failed to save promptware: {ex.Message}", "Error");
                    }
                })
            )
        ).Width(Size.Rem(35));
    }
}
