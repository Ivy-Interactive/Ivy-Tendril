using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class PushToVaultProjectRow(string projectName, IState<HashSet<string>> selectedProjects) : ViewBase
{
    public override object Build()
    {
        var isChecked = UseState(selectedProjects.Value.Contains(projectName));

        UseEffect(() =>
        {
            var contains = selectedProjects.Value.Contains(projectName);
            if (isChecked.Value != contains) isChecked.Set(contains);
        }, selectedProjects);

        UseEffect(() =>
        {
            var set = new HashSet<string>(selectedProjects.Value, StringComparer.OrdinalIgnoreCase);
            if (isChecked.Value) set.Add(projectName);
            else set.Remove(projectName);

            if (!set.SetEquals(selectedProjects.Value))
                selectedProjects.Set(set);
        }, isChecked);

        return isChecked.ToBoolInput(projectName);
    }
}

public class PushToVaultDialog(
    IState<bool> dialogOpen,
    List<string> availableProjects,
    string? defaultProject,
    IVaultService vaultService,
    IClientProvider client,
    Action onPushed) : ViewBase
{
    public override object? Build()
    {
        var selectedProjects = UseState(() =>
            !string.IsNullOrEmpty(defaultProject)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { defaultProject }
                : new HashSet<string>(availableProjects, StringComparer.OrdinalIgnoreCase));

        var version = UseState(() => vaultService.GenerateVersionTimestamp());
        var changelog = UseState("");
        var reviewers = UseState("");
        var isLoading = UseState(false);
        var errorMessage = UseState<string?>(null);
        var createdPrUrl = UseState<string?>(null);

        if (!dialogOpen.Value) return null;

        async Task HandlePush()
        {
            if (isLoading.Value || selectedProjects.Value.Count == 0) return;

            isLoading.Set(true);
            errorMessage.Set(null);

            var projectList = selectedProjects.Value.ToList();
            var reviewerList = reviewers.Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var prTitle = $"feat(vault): update {string.Join(", ", projectList)} to v{version.Value}";
            var prBody = $"### Vault Version Update: v{version.Value}\n\n**Changelog:**\n{changelog.Value.Trim()}\n\n**Projects Included:**\n{string.Join("\n", projectList.Select(p => $"- {p}"))}\n\n> Published from Ivy Tendril.";

            var request = new VaultExportRequest
            {
                ProjectNames = projectList,
                Version = version.Value,
                Changelog = changelog.Value.Trim(),
                PrTitle = prTitle,
                PrBody = prBody,
                Reviewers = reviewerList
            };

            var result = await vaultService.PushAndCreatePrAsync(request);
            isLoading.Set(false);

            if (result.Success)
            {
                createdPrUrl.Set(result.PrUrl);
                client.Toast($"Created PR for v{version.Value}", "Vault PR Created");
                onPushed();
            }
            else
            {
                errorMessage.Set(result.ErrorMessage ?? "Failed to create PR for vault update.");
            }
        }

        if (createdPrUrl.Value != null)
        {
            var successContent = Layout.Vertical()
                | Text.Block($"Version v{version.Value} has been published to a new branch!").Bold()
                | Text.P("A GitHub Pull Request has been opened for team review:").Small().Muted()
                | new Button(createdPrUrl.Value)
                    .Icon(Icons.GitPullRequest)
                    .Primary()
                    .OnClick(() => client.OpenUrl(createdPrUrl.Value));

            var successFooter = Layout.Horizontal().AlignContent(Align.Right)
                | new Button("Done").Outline().OnClick(() => dialogOpen.Set(false));

            return new Dialog(
                _ => dialogOpen.Set(false),
                new DialogHeader("Pull Request Created"),
                new DialogBody(successContent),
                new DialogFooter(successFooter)
            );
        }

        var projectCheckboxes = Layout.Vertical();
        foreach (var projName in availableProjects)
        {
            projectCheckboxes |= new PushToVaultProjectRow(projName, selectedProjects);
        }

        var form = Layout.Vertical()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | Text.Block("Version:").Bold().Small()
                | new Badge($"v{version.Value}").Variant(BadgeVariant.Secondary))
            | Text.P("Select the projects to publish to the team vault:").Small().Muted()
            | projectCheckboxes
            | changelog.ToTextareaInput("Describe what changed in this version (e.g. Added Playwright MCP and updated component-audit skill)...")
                .WithField().Label("Changelog")
            | reviewers.ToTextInput("octocat, mona (optional)")
                .WithField().Label("Request Reviewers (GitHub Usernames)")
            | Text.Block("🔒 Any API keys or auth tokens will be automatically replaced with ${VAR} placeholders.").Small().Muted()
            | (errorMessage.Value != null
                ? Text.Block(errorMessage.Value).Color(Colors.Destructive).Small()
                : null);

        var actions = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
            | new Button("Publish & Create PR").Primary()
                .Icon(Icons.GitPullRequest)
                .Loading(isLoading.Value)
                .Disabled(isLoading.Value || selectedProjects.Value.Count == 0 || string.IsNullOrWhiteSpace(changelog.Value))
                .OnClick(async () => await HandlePush());

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Publish to Team Vault"),
            new DialogBody(form),
            new DialogFooter(actions)
        );
    }
}
