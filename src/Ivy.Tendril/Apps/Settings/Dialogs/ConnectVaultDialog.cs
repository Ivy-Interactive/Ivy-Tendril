using System;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class ConnectVaultDialog(
    IState<bool> dialogOpen,
    IVaultService vaultService,
    IClientProvider client,
    Action onConnected) : ViewBase
{
    public override object? Build()
    {
        var repoUrl = UseState("");
        var customName = UseState("");
        var isLoading = UseState(false);
        var errorMessage = UseState<string?>(null);

        if (!dialogOpen.Value) return null;

        async Task HandleConnect()
        {
            if (isLoading.Value || string.IsNullOrWhiteSpace(repoUrl.Value)) return;

            isLoading.Set(true);
            errorMessage.Set(null);

            var result = await vaultService.ConnectVaultAsync(repoUrl.Value.Trim(), customName.Value.Trim());
            isLoading.Set(false);

            if (result.Success)
            {
                dialogOpen.Set(false);
                client.Toast(result.Message, "Vault Connected");
                onConnected();
            }
            else
            {
                errorMessage.Set(result.ErrorMessage ?? result.Message);
            }
        }

        var form = Layout.Vertical()
            | Text.P("Enter the Git clone URL of an existing Tendril Vault repository.").Small().Muted()
            | repoUrl.ToTextInput("https://github.com/my-org/Tendril-Vault.git")
                .WithField().Label("Git Repository URL")
            | customName.ToTextInput("e.g. Core Team Vault (optional)")
                .WithField().Label("Display Name (Optional)")
            | (errorMessage.Value != null
                ? Text.Block(errorMessage.Value).Color(Colors.Destructive).Small()
                : null);

        var actions = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
            | new Button("Connect Vault").Primary()
                .Loading(isLoading.Value)
                .Disabled(isLoading.Value || string.IsNullOrWhiteSpace(repoUrl.Value))
                .OnClick(async () => await HandleConnect());

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Connect Existing Team Vault"),
            new DialogBody(form),
            new DialogFooter(actions)
        );
    }
}
