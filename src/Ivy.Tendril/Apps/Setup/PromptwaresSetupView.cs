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
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<PromptwareRow, int>(idx =>
            {
                var name = rows[idx].Name;
                var isBuiltIn = Constants.JobTypes.BuiltIn.Contains(name);
                return Layout.Horizontal().Gap(1)
                       | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit this promptware").OnClick(() =>
                       {
                           showTrigger(name);
                       })
                       | (isBuiltIn
                           ? null
                           : new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete this promptware").OnClick(() =>
                           {
                               showAlert($"Are you sure you want to delete '{name}'?", result =>
                               {
                                   if (result == AlertResult.Ok)
                                   {
                                       promptwares.Remove(name);
                                       config.SaveSettings();
                                       client.Toast($"Promptware '{name}' deleted", "Deleted");
                                       refreshToken.Refresh();
                                   }
                               }, "Delete Promptware", AlertButtonSet.OkCancel);
                           }));
            }));

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(200)))
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
        UseEffect(() =>
        {
            var pw = config.Settings.Promptwares;
            if (existingKey != null && pw.ContainsKey(existingKey))
            {
                var p = pw[existingKey];
                editName.Set(existingKey);
                editProfile.Set(p.Profile);
                editAllowedTools.Set(string.Join(", ", p.AllowedTools));
            }
        }, EffectTrigger.OnMount());

        var promptwares = config.Settings.Promptwares;
        var isNew = existingKey == null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(isNew ? "Add Promptware" : "Edit Promptware"),
            new DialogBody(
                Layout.Vertical().Gap(4)
                | editName.ToTextInput("Promptware name (e.g. CreatePlan)...").WithField().Label("Name")
                | editProfile.ToTextInput("Profile name (e.g. deep, balanced, quick)...").WithField().Label("Profile")
                | editAllowedTools.ToTextInput("Comma-separated tools (e.g. Read, Write, Edit)...").WithField()
                    .Label("Allowed Tools")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;

                    var tools = editAllowedTools.Value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .ToList();

                    var pwConfig = new PromptwareConfig
                    {
                        Profile = editProfile.Value,
                        AllowedTools = tools
                    };

                    if (!isNew && existingKey != editName.Value)
                        promptwares.Remove(existingKey!);

                    promptwares[editName.Value] = pwConfig;
                    config.SaveSettings();
                    isOpen.Set(false);
                    refreshToken.Refresh();
                    client.Toast("Promptware saved", "Saved");
                })
            )
        ).Width(Size.Rem(35));
    }
}
