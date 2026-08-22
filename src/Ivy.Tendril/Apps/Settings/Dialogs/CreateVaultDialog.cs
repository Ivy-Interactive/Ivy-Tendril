using System;
using System.Collections.Generic;
using System.Linq;
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
        var selectedOwner = UseState<string?>(null);
        var isPrivate = UseState(true);
        var isLoading = UseState(false);
        var errorMessage = UseState<string?>(null);

        var accountsQuery = UseQuery<List<GitHubAccountOption>, string>(
            "github_accounts",
            async (_, _) => await vaultService.GetGitHubAccountsAndOrgsAsync());

        UseEffect(() =>
        {
            if (string.IsNullOrEmpty(selectedOwner.Value) && accountsQuery.Value != null && accountsQuery.Value.Count > 0)
            {
                selectedOwner.Set(accountsQuery.Value[0].Login);
            }
        }, accountsQuery);

        if (!dialogOpen.Value) return null;

        async Task HandleCreate()
        {
            if (isLoading.Value || string.IsNullOrWhiteSpace(repoName.Value)) return;

            isLoading.Set(true);
            errorMessage.Set(null);

            var chosenOrg = selectedOwner.Value?.Trim();
            Console.WriteLine($"[CreateVaultDialog] HandleCreate triggered: repoName='{repoName.Value}', selectedOwner='{selectedOwner.Value}', isPrivate={isPrivate.Value}");

            var result = await vaultService.CreateVaultRepoAsync(
                repoName.Value.Trim(),
                isPrivate.Value,
                string.IsNullOrWhiteSpace(chosenOrg) ? null : chosenOrg);

            isLoading.Set(false);

            Console.WriteLine($"[CreateVaultDialog] CreateVaultRepoAsync finished: Success={result.Success}, Message='{result.Message}', ErrorMessage='{result.ErrorMessage}'");

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

        var accounts = accountsQuery.Value ?? new List<GitHubAccountOption>();
        object ownerInput;

        if (accounts.Count > 0)
        {
            var options = accounts
                .Select(a => new Option<string>($"{a.Login} ({a.Type})", a.Login))
                .ToArray();

            ownerInput = selectedOwner.ToSelectInput(options)
                .Loading(accountsQuery.Loading)
                .WithField().Label("Owner / Organization");
        }
        else
        {
            ownerInput = selectedOwner.ToTextInput("e.g. username or organization")
                .WithField().Label("Owner / Organization");
        }

        var form = Layout.Vertical()
            | Text.P("Create a private GitHub repository to share Tendril project configs, custom skills, and MCP servers with your team.").Small().Muted()
            | repoName.ToTextInput("Tendril-Vault")
                .WithField().Label("Repository Name")
            | ownerInput
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
