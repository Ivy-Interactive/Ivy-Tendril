using System;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class CreateVaultDialog(
    IState<bool> dialogOpen,
    IVaultService vaultService,
    IClientProvider client,
    Action onCreated) : ViewBase
{
    public override object? Build()
    {
        var repoName = UseState("Tendril-Vault");
        var org = UseState("");
        var isPrivate = UseState(true);
        var isLoading = UseState(false);
        var errorMessage = UseState<string?>(null);

        if (!dialogOpen.Value) return null;

        async Task HandleCreate()
        {
            if (isLoading.Value || string.IsNullOrWhiteSpace(repoName.Value)) return;

            isLoading.Set(true);
            errorMessage.Set(null);

            var result = await vaultService.CreateVaultRepoAsync(
                repoName.Value.Trim(),
                isPrivate.Value,
                string.IsNullOrWhiteSpace(org.Value) ? null : org.Value.Trim());

            isLoading.Set(false);

            if (result.Success)
            {
                dialogOpen.Set(false);
                client.Toast(result.Message, "Vault Created");
                onCreated();
            }
            else
            {
                errorMessage.Set(result.ErrorMessage ?? result.Message);
            }
        }

        var form = Layout.Vertical()
            | Text.P("Create a private GitHub repository to share Tendril project configs, custom skills, and MCP servers with your team.").Small().Muted()
            | repoName.ToTextInput("Tendril-Vault")
                .WithField().Label("Repository Name")
            | org.ToTextInput("my-team (optional, blank for personal account)")
                .WithField().Label("Owner / Organization")
            | isPrivate.ToBoolInput("Private Repository")
            | (errorMessage.Value != null
                ? Text.Block(errorMessage.Value).Color(Colors.Destructive).Small()
                : null);

        var actions = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
            | new Button("Create Vault").Primary()
                .Loading(isLoading.Value)
                .Disabled(isLoading.Value || string.IsNullOrWhiteSpace(repoName.Value))
                .OnClick(async () => await HandleCreate());

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Create Team Vault on GitHub"),
            new DialogBody(form),
            new DialogFooter(actions)
        );
    }
}
