using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public enum ImportAssetKind
{
    McpServers,
    Skills
}

public class ImportRepoAssetsDialog(
    IState<bool> isOpen,
    ImportAssetKind kind,
    string projectName,
    List<RepoRef> projectRepos,
    IConfigService config,
    IClientProvider client,
    IState<List<ProjectMcpServerRef>>? mcpServers = null,
    IState<List<ProjectSkillRef>>? skills = null) : ViewBase
{
    public override object? Build()
    {
        var selectedRepo = UseState(() =>
        {
            if (projectRepos.Count > 0)
                return projectRepos[0].Path;
            return "";
        });
        var customPath = UseState("");
        var isCustomPath = UseState(() => projectRepos.Count == 0);
        var selectedItemNames = UseState(() => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var scanVersion = UseState(0);

        if (!isOpen.Value) return null;

        string GetEffectiveRepoPath()
        {
            if (isCustomPath.Value)
            {
                return VariableExpansion.ExpandVariables(customPath.Value.Trim(), config.TendrilHome ?? "");
            }

            var repo = projectRepos.FirstOrDefault(r => r.Path == selectedRepo.Value);
            var rawPath = repo?.Path ?? selectedRepo.Value;
            return VariableExpansion.ExpandVariables(rawPath, config.TendrilHome ?? "");
        }

        var effectivePath = GetEffectiveRepoPath();
        var isPathValid = !string.IsNullOrWhiteSpace(effectivePath) && Directory.Exists(effectivePath);

        var discoveredMcp = kind == ImportAssetKind.McpServers && isPathValid
            ? RepoAssetScanner.ScanMcpServers(effectivePath)
            : new List<DiscoveredMcpServer>();

        var discoveredSkills = kind == ImportAssetKind.Skills && isPathValid
            ? RepoAssetScanner.ScanSkills(effectivePath)
            : new List<DiscoveredSkill>();

        var allItemNames = kind == ImportAssetKind.McpServers
            ? discoveredMcp.Select(m => m.Name).ToList()
            : discoveredSkills.Select(s => s.Name).ToList();

        void ToggleSelectAll()
        {
            if (selectedItemNames.Value.Count == allItemNames.Count && allItemNames.Count > 0)
            {
                selectedItemNames.Set(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                selectedItemNames.Set(new HashSet<string>(allItemNames, StringComparer.OrdinalIgnoreCase));
            }
        }

        // Track when repo select changes to custom
        UseEffect(() =>
        {
            if (selectedRepo.Value == "__custom__")
                isCustomPath.Set(true);
            else
                isCustomPath.Set(false);
        }, selectedRepo);

        // Initialize selection when scan changes
        UseEffect(() =>
        {
            selectedItemNames.Set(new HashSet<string>(allItemNames, StringComparer.OrdinalIgnoreCase));
        }, [selectedRepo, customPath, isCustomPath, scanVersion]);

        void ExecuteImport()
        {
            var selectedSet = selectedItemNames.Value;
            if (selectedSet.Count == 0) return;

            if (kind == ImportAssetKind.McpServers && mcpServers != null)
            {
                var toImport = discoveredMcp.Where(m => selectedSet.Contains(m.Name)).ToList();
                var list = new List<ProjectMcpServerRef>(mcpServers.Value);
                foreach (var srv in toImport)
                {
                    var existing = list.FirstOrDefault(m => m.Name.Equals(srv.Name, StringComparison.OrdinalIgnoreCase));
                    var mcpRef = RepoAssetScanner.ImportMcpServer(srv);
                    if (existing != null)
                        list[list.IndexOf(existing)] = mcpRef;
                    else
                        list.Add(mcpRef);
                }
                mcpServers.Set(list);
                client.Toast($"Imported {toImport.Count} MCP server(s)", "Imported");
            }
            else if (kind == ImportAssetKind.Skills && skills != null)
            {
                var toImport = discoveredSkills.Where(s => selectedSet.Contains(s.Name)).ToList();
                var list = new List<ProjectSkillRef>(skills.Value);
                foreach (var sk in toImport)
                {
                    var existing = list.FirstOrDefault(k => k.Name.Equals(sk.Name, StringComparison.OrdinalIgnoreCase));
                    var skillRef = RepoAssetScanner.ImportSkillToProject(config.TendrilHome ?? "", projectName, sk, copyFiles: true);
                    if (existing != null)
                        list[list.IndexOf(existing)] = skillRef;
                    else
                        list.Add(skillRef);
                }
                skills.Set(list);
                client.Toast($"Imported {toImport.Count} custom skill(s)", "Imported");
            }

            isOpen.Set(false);
        }

        var title = kind == ImportAssetKind.McpServers
            ? "Import MCP Tools & Servers from Repository"
            : "Import Custom Skills from Repository";

        var repoOptions = projectRepos.Select(r => new Option<string>(r.Path, Path.GetFileName(r.Path) ?? r.Path)).ToList();
        repoOptions.Add(new Option<string>("__custom__", "Custom Folder Path..."));

        var repoSelectorLayout = Layout.Vertical()
            | Text.Block("Select Repository Source").Bold().Small();

        if (projectRepos.Count > 0)
        {
            repoSelectorLayout |= selectedRepo.ToSelectInput(repoOptions);
        }

        if (isCustomPath.Value || projectRepos.Count == 0)
        {
            repoSelectorLayout |= customPath.ToTextInput("Enter local folder path to repository (e.g. ~/my-repo)...").WithField().Label("Folder Path");
        }

        var resultsLayout = Layout.Vertical();

        if (!isPathValid)
        {
            resultsLayout |= Text.Block("Select or enter a valid repository directory to scan.").Muted().Small();
        }
        else if (kind == ImportAssetKind.McpServers)
        {
            if (discoveredMcp.Count == 0)
            {
                resultsLayout |= Text.Block("No MCP configuration files (.mcp.json, mcp_config.json, .vscode/mcp.json) found in this repository.").Muted().Small();
            }
            else
            {
                var selectAllBtn = new Button(selectedItemNames.Value.Count == allItemNames.Count ? "Deselect All" : "Select All")
                    .Ghost().Small().OnClick(ToggleSelectAll);

                resultsLayout |= (Layout.Horizontal().AlignContent(Align.Left)
                    | Text.Block($"Discovered MCP Servers ({discoveredMcp.Count})").Bold().Small()
                    | selectAllBtn);

                var itemsList = Layout.Vertical();
                foreach (var srv in discoveredMcp)
                {
                    var argsStr = srv.Arguments.Count > 0 ? " " + string.Join(" ", srv.Arguments) : "";
                    itemsList |= new DiscoveredItemRowView(
                        srv.Name,
                        srv.SourceFilePath,
                        $"{srv.Command}{argsStr}",
                        selectedItemNames);
                }
                resultsLayout |= itemsList;
            }
        }
        else if (kind == ImportAssetKind.Skills)
        {
            if (discoveredSkills.Count == 0)
            {
                resultsLayout |= Text.Block("No SKILL.md files found in this repository (.agents/skills, .gemini/skills, skills/, etc.).").Muted().Small();
            }
            else
            {
                var selectAllBtn = new Button(selectedItemNames.Value.Count == allItemNames.Count ? "Deselect All" : "Select All")
                    .Ghost().Small().OnClick(ToggleSelectAll);

                resultsLayout |= (Layout.Horizontal().AlignContent(Align.Left)
                    | Text.Block($"Discovered Custom Skills ({discoveredSkills.Count})").Bold().Small()
                    | selectAllBtn);

                var itemsList = Layout.Vertical();
                foreach (var sk in discoveredSkills)
                {
                    itemsList |= new DiscoveredItemRowView(
                        sk.Name,
                        sk.RelativePath,
                        sk.Description,
                        selectedItemNames);
                }
                resultsLayout |= itemsList;
            }
        }

        var totalDiscovered = kind == ImportAssetKind.McpServers ? discoveredMcp.Count : discoveredSkills.Count;
        var selectedCount = selectedItemNames.Value.Count;

        var body = Layout.Vertical()
            | repoSelectorLayout
            | new Separator()
            | resultsLayout;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(title),
            new DialogBody(body),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button($"Import Selected ({selectedCount})").Primary().Disabled(selectedCount == 0).OnClick(ExecuteImport)
            )
        ).Width(Size.Rem(36));
    }
}

public class DiscoveredItemRowView(
    string name,
    string badgeText,
    string description,
    IState<HashSet<string>> selectedItems) : ViewBase
{
    public override object? Build()
    {
        var isChecked = UseState(() => selectedItems.Value.Contains(name));

        UseEffect(() =>
        {
            var contains = selectedItems.Value.Contains(name);
            if (isChecked.Value != contains)
                isChecked.Set(contains);
        }, selectedItems);

        UseEffect(() =>
        {
            var set = new HashSet<string>(selectedItems.Value, StringComparer.OrdinalIgnoreCase);
            if (isChecked.Value)
                set.Add(name);
            else
                set.Remove(name);

            if (!set.SetEquals(selectedItems.Value))
                selectedItems.Set(set);
        }, isChecked);

        return Layout.Horizontal().AlignContent(Align.Left)
            | isChecked.ToBoolInput()
            | (Layout.Vertical()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | Text.Block(name).Bold().Small()
                    | new Badge(badgeText).Variant(BadgeVariant.Outline).Small())
                | Text.Block(description).Muted().Small());
    }
}
