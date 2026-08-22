using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class PushProjectSelectRow(
    string projName,
    IState<HashSet<string>> selectedProjects) : ViewBase
{
    public override object Build()
    {
        var isChecked = UseState(() => selectedProjects.Value.Contains(projName));

        UseEffect(() =>
        {
            var contains = selectedProjects.Value.Contains(projName);
            if (isChecked.Value != contains) isChecked.Set(contains);
        }, selectedProjects);

        UseEffect(() =>
        {
            var set = new HashSet<string>(selectedProjects.Value, StringComparer.OrdinalIgnoreCase);
            if (isChecked.Value) set.Add(projName);
            else set.Remove(projName);

            if (!set.SetEquals(selectedProjects.Value)) selectedProjects.Set(set);
        }, isChecked);

        return isChecked.ToBoolInput(projName);
    }
}

public class PushProjectHeaderBadge(
    string projName,
    List<string> projSkills,
    List<string> projMcps,
    List<string> projMemories,
    List<string> projActions,
    List<string> projVerifs,
    IState<Dictionary<string, HashSet<string>>> selectedSkills,
    IState<Dictionary<string, HashSet<string>>> selectedMcps,
    IState<Dictionary<string, HashSet<string>>> selectedMemories,
    IState<Dictionary<string, HashSet<string>>> selectedReviewActions,
    IState<Dictionary<string, HashSet<string>>> selectedVerifications) : ViewBase
{
    public override object Build()
    {
        var selSkillsCount = selectedSkills.Value.TryGetValue(projName, out var s) ? s.Count : projSkills.Count;
        var selMcpsCount = selectedMcps.Value.TryGetValue(projName, out var m) ? m.Count : projMcps.Count;
        var selMemsCount = selectedMemories.Value.TryGetValue(projName, out var mem) ? mem.Count : projMemories.Count;
        var selActionsCount = selectedReviewActions.Value.TryGetValue(projName, out var a) ? a.Count : projActions.Count;
        var selVerifsCount = selectedVerifications.Value.TryGetValue(projName, out var v) ? v.Count : projVerifs.Count;

        var parts = new List<string>();
        if (projSkills.Count > 0) parts.Add($"{selSkillsCount}/{projSkills.Count} skills");
        if (projMcps.Count > 0) parts.Add($"{selMcpsCount}/{projMcps.Count} MCPs");
        if (projMemories.Count > 0) parts.Add($"{selMemsCount}/{projMemories.Count} mems");
        if (projActions.Count > 0) parts.Add($"{selActionsCount}/{projActions.Count} actions");
        if (projVerifs.Count > 0) parts.Add($"{selVerifsCount}/{projVerifs.Count} verifs");

        var text = parts.Count > 0 ? string.Join(" • ", parts) : "0 assets";
        return new Badge(text).Variant(BadgeVariant.Secondary).Small();
    }
}

public class PushAssetItemRow(
    string name,
    string projName,
    IState<Dictionary<string, HashSet<string>>> dictState,
    string? badge = null) : ViewBase
{
    public override object Build()
    {
        var isChecked = UseState(() =>
            dictState.Value.TryGetValue(projName, out var set) && set.Contains(name));

        UseEffect(() =>
        {
            var contains = dictState.Value.TryGetValue(projName, out var set) && set.Contains(name);
            if (isChecked.Value != contains) isChecked.Set(contains);
        }, dictState);

        UseEffect(() =>
        {
            var nextDict = new Dictionary<string, HashSet<string>>(dictState.Value);
            var nextSet = new HashSet<string>(nextDict.TryGetValue(projName, out var s) ? s : new(), StringComparer.OrdinalIgnoreCase);
            if (isChecked.Value) nextSet.Add(name);
            else nextSet.Remove(name);
            nextDict[projName] = nextSet;
            dictState.Set(nextDict);
        }, isChecked);

        return Layout.Horizontal().AlignContent(Align.Left)
            | isChecked.ToBoolInput(name)
            | (badge != null ? new Badge(badge).Variant(BadgeVariant.Outline).Small() : null);
    }
}

public class PushProjectPermissionsRow(
    string projName,
    IState<Dictionary<string, bool>> syncPermissions) : ViewBase
{
    public override object Build()
    {
        var isChecked = UseState(() =>
            syncPermissions.Value.TryGetValue(projName, out var p) ? p : true);

        UseEffect(() =>
        {
            var contains = syncPermissions.Value.TryGetValue(projName, out var p) ? p : true;
            if (isChecked.Value != contains) isChecked.Set(contains);
        }, syncPermissions);

        UseEffect(() =>
        {
            var nextDict = new Dictionary<string, bool>(syncPermissions.Value) { [projName] = isChecked.Value };
            syncPermissions.Set(nextDict);
        }, isChecked);

        return isChecked.ToBoolInput("Include Security & Permissions Policies");
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
        var config = UseService<IConfigService>();

        var selectedProjects = UseState(() =>
            !string.IsNullOrEmpty(defaultProject)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { defaultProject }
                : new HashSet<string>(availableProjects, StringComparer.OrdinalIgnoreCase));

        var selectedSkills = UseState<Dictionary<string, HashSet<string>>>(() =>
        {
            var dict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var projName in availableProjects)
            {
                var proj = config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(projName, StringComparison.OrdinalIgnoreCase));
                var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (proj != null)
                {
                    foreach (var s in proj.Skills) skills.Add(s.Name);
                    var skillsDir = ProjectPathHelper.GetSkillsDir(config.TendrilHome, proj.Name);
                    if (Directory.Exists(skillsDir))
                    {
                        foreach (var f in Directory.GetFiles(skillsDir, "*.md"))
                            skills.Add(Path.GetFileNameWithoutExtension(f));
                    }
                }
                dict[projName] = skills;
            }
            return dict;
        });

        var selectedMcps = UseState<Dictionary<string, HashSet<string>>>(() =>
        {
            var dict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var projName in availableProjects)
            {
                var proj = config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(projName, StringComparison.OrdinalIgnoreCase));
                var mcps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (proj != null)
                {
                    foreach (var m in proj.McpServers) mcps.Add(m.Name);
                }
                dict[projName] = mcps;
            }
            return dict;
        });

        var selectedMemories = UseState<Dictionary<string, HashSet<string>>>(() =>
        {
            var dict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var projName in availableProjects)
            {
                var mems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var memoryDir = ProjectPathHelper.GetMemoryDir(config.TendrilHome, projName);
                if (Directory.Exists(memoryDir))
                {
                    foreach (var f in Directory.GetFiles(memoryDir, "*.md"))
                        mems.Add(Path.GetFileName(f));
                }
                dict[projName] = mems;
            }
            return dict;
        });

        var selectedReviewActions = UseState<Dictionary<string, HashSet<string>>>(() =>
        {
            var dict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var projName in availableProjects)
            {
                var proj = config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(projName, StringComparison.OrdinalIgnoreCase));
                var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (proj != null)
                {
                    foreach (var a in proj.ReviewActions) actions.Add(a.Name);
                }
                dict[projName] = actions;
            }
            return dict;
        });

        var selectedVerifications = UseState<Dictionary<string, HashSet<string>>>(() =>
        {
            var dict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var projName in availableProjects)
            {
                var proj = config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(projName, StringComparison.OrdinalIgnoreCase));
                var verifs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (proj != null)
                {
                    foreach (var v in proj.Verifications) verifs.Add(v.Name);
                }
                dict[projName] = verifs;
            }
            return dict;
        });

        var syncPermissions = UseState<Dictionary<string, bool>>(() =>
        {
            var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var projName in availableProjects) dict[projName] = true;
            return dict;
        });

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
                Reviewers = reviewerList,
                SelectedSkills = selectedSkills.Value.ToDictionary(k => k.Key, v => v.Value.ToList()),
                SelectedMcps = selectedMcps.Value.ToDictionary(k => k.Key, v => v.Value.ToList()),
                SelectedMemories = selectedMemories.Value.ToDictionary(k => k.Key, v => v.Value.ToList()),
                SelectedReviewActions = selectedReviewActions.Value.ToDictionary(k => k.Key, v => v.Value.ToList()),
                SelectedVerifications = selectedVerifications.Value.ToDictionary(k => k.Key, v => v.Value.ToList()),
                SyncPermissions = syncPermissions.Value
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

        var projectSelectorList = Layout.Vertical();
        foreach (var projName in availableProjects)
        {
            var isProjectChecked = selectedProjects.Value.Contains(projName);
            var proj = config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(projName, StringComparison.OrdinalIgnoreCase));

            var projSkills = new List<string>();
            var projMcps = new List<string>();
            var projMemories = new List<string>();
            var projActions = new List<string>();
            var projVerifs = new List<string>();

            if (proj != null)
            {
                foreach (var s in proj.Skills) projSkills.Add(s.Name);
                var skillsDir = ProjectPathHelper.GetSkillsDir(config.TendrilHome, proj.Name);
                if (Directory.Exists(skillsDir))
                {
                    foreach (var f in Directory.GetFiles(skillsDir, "*.md"))
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        if (!projSkills.Contains(name)) projSkills.Add(name);
                    }
                }

                foreach (var m in proj.McpServers) projMcps.Add(m.Name);

                var memDir = ProjectPathHelper.GetMemoryDir(config.TendrilHome, proj.Name);
                if (Directory.Exists(memDir))
                {
                    foreach (var f in Directory.GetFiles(memDir, "*.md"))
                        projMemories.Add(Path.GetFileName(f));
                }

                foreach (var a in proj.ReviewActions) projActions.Add(a.Name);
                foreach (var v in proj.Verifications) projVerifs.Add(v.Name);
            }

            var projectHeader = Layout.Horizontal().AlignContent(Align.Left)
                | new PushProjectSelectRow(projName, selectedProjects)
                | new PushProjectHeaderBadge(
                    projName,
                    projSkills,
                    projMcps,
                    projMemories,
                    projActions,
                    projVerifs,
                    selectedSkills,
                    selectedMcps,
                    selectedMemories,
                    selectedReviewActions,
                    selectedVerifications);

            var assetContent = Layout.Vertical();

            // Skills Section
            if (projSkills.Count > 0)
            {
                var skillsList = Layout.Vertical();
                foreach (var sName in projSkills)
                {
                    skillsList |= new PushAssetItemRow(sName, projName, selectedSkills, "Skill");
                }
                assetContent |= new Expandable($"Skills ({projSkills.Count})", skillsList).Small().Open(true);
            }

            // MCP Servers Section
            if (projMcps.Count > 0)
            {
                var mcpsList = Layout.Vertical();
                foreach (var mName in projMcps)
                {
                    mcpsList |= new PushAssetItemRow(mName, projName, selectedMcps, "MCP");
                }
                assetContent |= new Expandable($"MCP Servers ({projMcps.Count})", mcpsList).Small().Open(true);
            }

            // Memories Section
            if (projMemories.Count > 0)
            {
                var memsList = Layout.Vertical();
                foreach (var memName in projMemories)
                {
                    memsList |= new PushAssetItemRow(memName, projName, selectedMemories, "Memory");
                }
                assetContent |= new Expandable($"Project Memories ({projMemories.Count})", memsList).Small().Open(true);
            }

            // Review Actions Section
            if (projActions.Count > 0)
            {
                var actionsList = Layout.Vertical();
                foreach (var aName in projActions)
                {
                    actionsList |= new PushAssetItemRow(aName, projName, selectedReviewActions, "Action");
                }
                assetContent |= new Expandable($"Review Actions ({projActions.Count})", actionsList).Small().Open(true);
            }

            // Verifications Section
            if (projVerifs.Count > 0)
            {
                var verifsList = Layout.Vertical();
                foreach (var vName in projVerifs)
                {
                    verifsList |= new PushAssetItemRow(vName, projName, selectedVerifications, "Verification");
                }
                assetContent |= new Expandable($"Verifications ({projVerifs.Count})", verifsList).Small().Open(true);
            }

            // Permissions Policy
            assetContent |= new PushProjectPermissionsRow(projName, syncPermissions);

            var projectCard = new Expandable(projectHeader, assetContent)
                .Small()
                .Open(isProjectChecked);

            projectSelectorList |= projectCard;
        }

        var form = Layout.Vertical()
            | Text.Block("Projects & Assets to Publish").Small().Bold()
            | projectSelectorList
            | Text.Block("Release Details").Small().Bold()
            | version.ToTextInput().WithField().Label("Version Tag (UTC Timestamp)")
            | changelog.ToTextareaInput("Summary of updates, new skills, MCP servers, or security policy changes...")
                .WithField().Label("Changelog / Release Notes")
            | reviewers.ToTextInput("e.g. alice, bob (comma-separated GitHub usernames)")
                .WithField().Label("Request PR Reviewers")
            | (errorMessage.Value != null ? Callout.Error(errorMessage.Value) : null)
            | (createdPrUrl.Value != null
                ? Callout.Success($"Pull request opened successfully: {createdPrUrl.Value}")
                : null);

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Publish to Team Vault (Create PR)"),
            new DialogBody(form),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button("Publish & Open PR")
                    .Icon(Icons.GitPullRequest)
                    .Primary()
                    .Loading(isLoading.Value)
                    .Disabled(selectedProjects.Value.Count == 0 || isLoading.Value)
                    .OnClick(async () => await HandlePush())
            )
        );
    }
}
