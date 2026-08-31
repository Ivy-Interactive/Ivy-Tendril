using System;
using System.Collections.Generic;
using System.Linq;
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
        var selectedDetectedVault = UseState("");
        var isLoading = UseState(false);
        var errorMessage = UseState<string?>(null);

        var discoveredQuery = UseQuery<List<DiscoveredVaultRepo>, string>(
            "discovered_vaults",
            async (_, _) => await vaultService.DiscoverExistingVaultsAsync());

        var discoveredList = discoveredQuery.Value ?? new List<DiscoveredVaultRepo>();

        UseEffect(() =>
        {
            if (!string.IsNullOrEmpty(selectedDetectedVault.Value))
            {
                var matched = discoveredList.FirstOrDefault(d => d.RepoUrl == selectedDetectedVault.Value);
                if (matched != null)
                {
                    repoUrl.Set(matched.RepoUrl);
                    customName.Set(matched.FullName);
                }
            }
        }, selectedDetectedVault);

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

        object? detectedSelector = null;
        if (discoveredQuery.Loading)
        {
            detectedSelector = Text.Block("🔍 Searching your GitHub account and organizations for existing vaults...").Small().Muted();
        }
        else if (discoveredList.Count > 0)
        {
            var selectOptions = new List<Option<string>>
            {
                new Option<string>("-- Choose a detected GitHub vault (optional) --", "")
            };
            foreach (var d in discoveredList)
            {
                var label = $"{d.FullName} ({d.AccountType}{(d.IsPrivate ? ", private" : "")})";
                selectOptions.Add(new Option<string>(label, d.RepoUrl));
            }

            detectedSelector = selectedDetectedVault.ToSelectInput(selectOptions.ToArray())
                .WithField().Label("Detected GitHub Vaults");
        }

        var form = Layout.Vertical()
            | Text.P("Connect an existing Tendril Vault repository to synchronize projects, skills, and configuration with your team.").Small().Muted()
            | detectedSelector
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
