using Ivy.Tendril.Apps.Setup.Dialogs;
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
        var editIndex = UseState<int?>(-1);

        var verifications = config.Settings.Verifications;

        var rows = verifications.Select((v, i) => new VerificationRow(v.Name, v.Prompt, i)).ToList();

        var table = new TableBuilder<VerificationRow>(rows)
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<VerificationRow, int>(idx =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit this verification").OnClick(() =>
                {
                    editIndex.Set(idx);
                })
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete this verification").OnClick(() =>
                {
                    var name = verifications[idx].Name;
                    verifications.RemoveAt(idx);
                    config.SaveSettings();
                    client.Toast($"Verification '{name}' deleted", "Deleted");
                    refreshToken.Refresh();
                })
            ))
            .ColumnWidth(t => t.Name, Size.Units(32))
            .ColumnWidth(t => t.Prompt, Size.Units(100))
            .Multiline(t => t.Prompt)
            .ColumnWidth(t => t.Index, Size.Units(20));

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(400)))
               | Text.Block("Verification Definitions").Bold()
               | Text.Block("Define verification steps that run after plan execution.").Muted().Small()
               | table
               | new Button("Add Verification").Icon(Icons.Plus).Outline().OnClick(() =>
               {
                   editIndex.Set(null);
               })
               | new EditVerificationDialog(editIndex, verifications, config, client, refreshToken);
    }

    private record VerificationRow(string Name, string Prompt, int Index);
}
