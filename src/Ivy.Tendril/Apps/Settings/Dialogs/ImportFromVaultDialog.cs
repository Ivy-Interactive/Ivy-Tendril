using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class ImportRepoPathRow(string repo, IState<Dictionary<string, string>> repoMappings) : ViewBase
{
    public override object Build()
    {
        var inputState = UseState(() => repoMappings.Value.TryGetValue(repo, out var p) ? p : "");

        UseEffect(() =>
        {
            if (repoMappings.Value.TryGetValue(repo, out var val) && val != inputState.Value)
            {
                inputState.Set(val);
            }
        }, repoMappings);

        UseEffect(() =>
        {
            var updated = new Dictionary<string, string>(repoMappings.Value) { [repo] = inputState.Value };
            repoMappings.Set(updated);
        }, inputState);

        return inputState.ToTextInput().WithField().Label(repo);
    }
}

public class ImportFromVaultDialog(
    IState<bool> dialogOpen,
    VaultCatalogItem? projectItem,
    IVaultService vaultService,
    IClientProvider client,
    Action onImported) : ViewBase
{
    public override object? Build()
    {
        var repoMappings = UseState(() =>
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var initialMappings = new Dictionary<string, string>();
            if (projectItem != null)
            {
                foreach (var repo in projectItem.Repos)
                {
                    var repoName = repo.Contains('/') ? repo.Split('/')[^1] : repo;
                    initialMappings[repo] = Path.Combine(homeDir, "git", repoName);
                }
            }
            return initialMappings;
        });

        var isLoading = UseState(false);
        var errorMessage = UseState<string?>(null);

        if (!dialogOpen.Value || projectItem == null) return null;

        async Task HandleImport()
        {
            if (isLoading.Value) return;

            isLoading.Set(true);
            errorMessage.Set(null);

            var result = await vaultService.ImportProjectAsync(projectItem.Name, repoMappings.Value);
            isLoading.Set(false);

            if (result.Success)
            {
                dialogOpen.Set(false);
                client.Toast(result.Message, "Project Imported");
                onImported();
            }
            else
            {
                errorMessage.Set(result.ErrorMessage ?? result.Message);
            }
        }

        var repoInputs = Layout.Vertical();
        if (projectItem.Repos.Count > 0)
        {
            repoInputs |= Text.Block("Map repositories to local folders:").Small().Bold();
            foreach (var repo in projectItem.Repos)
            {
                repoInputs |= new ImportRepoPathRow(repo, repoMappings);
            }
        }

        var form = Layout.Vertical()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | Text.Block($"Project: {projectItem.Name}").Bold()
                | new Badge($"v{projectItem.RemoteVersion}").Variant(BadgeVariant.Secondary))
            | (!string.IsNullOrEmpty(projectItem.LatestChangelog)
                ? Text.P($"Changelog: {projectItem.LatestChangelog}").Small().Muted()
                : null)
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Badge($"{projectItem.ReposCount} Repos").Variant(BadgeVariant.Outline)
                | new Badge($"{projectItem.SkillsCount} Skills").Variant(BadgeVariant.Outline)
                | new Badge($"{projectItem.McpsCount} MCPs").Variant(BadgeVariant.Outline))
            | repoInputs
            | (errorMessage.Value != null
                ? Text.Block(errorMessage.Value).Color(Colors.Destructive).Small()
                : null);

        var actions = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
            | new Button("Import Project").Primary()
                .Loading(isLoading.Value)
                .Disabled(isLoading.Value)
                .OnClick(async () => await HandleImport());

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader($"Import '{projectItem.Name}' from Vault"),
            new DialogBody(form),
            new DialogFooter(actions)
        );
    }
}
