using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Setup;

public class VerificationsSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();
        var (triggerView, showTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new EditVerificationDialogContent(isOpen, existingIndex, config, client, refreshToken));
        var (alertView, showAlert) = UseAlert();

        var verifications = config.Settings.Verifications;

        var rows = verifications.Select((v, i) => new VerificationRow(v.Name, i)).ToList();

        var table = new TableBuilder<VerificationRow>(rows)
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<VerificationRow, int>(idx =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit this verification").OnClick(() =>
                {
                    showTrigger(idx);
                })
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete this verification").OnClick(() =>
                {
                    var name = verifications[idx].Name;
                    showAlert($"Are you sure you want to delete '{name}'?", result =>
                    {
                        if (result == AlertResult.Ok)
                        {
                            verifications.RemoveAt(idx);
                            config.SaveSettings();
                            client.Toast($"Verification '{name}' deleted", "Deleted");
                            refreshToken.Refresh();
                        }
                    }, "Delete Verification", AlertButtonSet.OkCancel);
                })
            ))
            .Width(Size.Fit());

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(400)))
               | Text.Block("Verification Definitions").Bold()
               | Text.Block("Define verification steps that run after plan execution.").Muted().Small()
               | table
               | new Button("Add Verification").Icon(Icons.Plus).Outline().OnClick(() =>
               {
                   showTrigger(null);
               })
               | triggerView
               | alertView;
    }

    private record VerificationRow(string Name, int Index);
}

file class EditVerificationDialogContent(
    IState<bool> isOpen,
    int? existingIndex,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editPrompt = UseState("");
        UseEffect(() =>
        {
            var verifications = config.Settings.Verifications;
            if (existingIndex != null && existingIndex >= 0 && existingIndex < verifications.Count)
            {
                editName.Set(verifications[existingIndex.Value].Name);
                editPrompt.Set(verifications[existingIndex.Value].Prompt);
            }
        }, EffectTrigger.OnMount());

        var verifications = config.Settings.Verifications;
        var isNew = existingIndex == null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(isNew ? "Add Verification" : "Edit Verification"),
            new DialogBody(
                Layout.Vertical().Gap(4)
                | editName.ToTextInput("Verification name...").WithField().Label("Name")
                | editPrompt.ToTextareaInput("Verification prompt...").Rows(8).WithField().Label("Prompt")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    if (isNew)
                    {
                        verifications.Add(new VerificationConfig
                        {
                            Name = editName.Value,
                            Prompt = editPrompt.Value
                        });
                    }
                    else
                    {
                        verifications[existingIndex!.Value].Name = editName.Value;
                        verifications[existingIndex!.Value].Prompt = editPrompt.Value;
                    }

                    config.SaveSettings();
                    isOpen.Set(false);
                    refreshToken.Refresh();
                    client.Toast("Verification saved", "Saved");
                })
            )
        ).Width(Size.Rem(35));
    }
}
