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
        var isLoading = UseState(false);
        var errorMessage = UseState<string?>(null);

        var discoveredQuery = UseQuery<List<DiscoveredVaultRepo>, string>(
            "discovered_vaults",
            async (_, _) => await vaultService.DiscoverExistingVaultsAsync());

        if (!dialogOpen.Value) return null;

        var discoveredList = discoveredQuery.Value ?? new List<DiscoveredVaultRepo>();

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

        var detectedSection = Layout.Vertical();
        if (discoveredQuery.Loading)
        {
            detectedSection |= Text.Block("🔍 Searching your GitHub account and organizations for existing vaults...").Small().Muted();
        }
        else if (discoveredList.Count > 0)
        {
            detectedSection |= Text.Block("Detected GitHub Vaults").Small().Bold();
            foreach (var disc in discoveredList)
            {
                var isSelected = repoUrl.Value.Equals(disc.RepoUrl, StringComparison.OrdinalIgnoreCase) ||
                                 repoUrl.Value.Equals(disc.FullName, StringComparison.OrdinalIgnoreCase);

                detectedSection |= Layout.Horizontal().AlignContent(Align.SpaceBetween)
                    | (Layout.Horizontal().AlignContent(Align.Left)
                        | Icons.FolderGit2.ToIcon()
                        | Text.Block(disc.FullName).Bold()
                        | new Badge(disc.AccountType).Variant(BadgeVariant.Secondary).Small()
                        | (disc.IsPrivate ? new Badge("Private").Variant(BadgeVariant.Outline).Small() : null))
                    | new Button(isSelected ? "Selected ✓" : "Select")
                        .Small()
                        .Variant(isSelected ? ButtonVariant.Secondary : ButtonVariant.Outline)
                        .OnClick(() =>
                        {
                            repoUrl.Set(disc.RepoUrl);
                            if (string.IsNullOrWhiteSpace(customName.Value))
                            {
                                customName.Set(disc.Name);
                            }
                        });
            }
        }

        var form = Layout.Vertical()
            | Text.P("Connect an existing Tendril Vault repository to synchronize projects, skills, and configuration with your team.").Small().Muted()
            | detectedSection
            | (discoveredList.Count > 0 ? Text.Block("Or Enter Repository URL Manually").Small().Bold() : null)
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
