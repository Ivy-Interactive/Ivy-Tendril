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
        var selectedDetectedVault = UseState<string?>(() => null);
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
                var list = discoveredQuery.Value ?? new List<DiscoveredVaultRepo>();
                var matched = list.FirstOrDefault(d => d.RepoUrl == selectedDetectedVault.Value);
                if (matched != null)
                {
                    repoUrl.Set(matched.RepoUrl);
                    if (string.IsNullOrWhiteSpace(customName.Value) || list.Any(d => d.FullName == customName.Value))
                    {
                        customName.Set(matched.FullName);
                    }
                }
                else
                {
                    repoUrl.Set(selectedDetectedVault.Value);
                }
            }
        }, selectedDetectedVault);

        if (!dialogOpen.Value) return null;

        var effectiveUrl = !string.IsNullOrWhiteSpace(repoUrl.Value)
            ? repoUrl.Value.Trim()
            : (!string.IsNullOrWhiteSpace(selectedDetectedVault.Value) ? selectedDetectedVault.Value.Trim() : "");

        async Task HandleConnect()
        {
            if (isLoading.Value || string.IsNullOrWhiteSpace(effectiveUrl)) return;

            isLoading.Set(true);
            errorMessage.Set(null);

            var result = await vaultService.ConnectVaultAsync(effectiveUrl, customName.Value.Trim());
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
            var selectOptions = discoveredList
                .Select(d => new Option<string>($"{d.FullName} ({d.AccountType}{(d.IsPrivate ? ", private" : "")})", d.RepoUrl))
                .ToArray();

            detectedSelector = selectedDetectedVault.ToSelectInput(selectOptions)
                .Placeholder("Select Repository")
                .Nullable()
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

        var isSubmitDisabled = isLoading.Value || string.IsNullOrWhiteSpace(effectiveUrl);

        var actions = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
            | new Button("Connect Vault").Primary()
                .Loading(isLoading.Value)
                .Disabled(isSubmitDisabled)
                .OnClick(async () => await HandleConnect());

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Connect Existing Team Vault"),
            new DialogBody(form),
            new DialogFooter(actions)
        );
    }
}
