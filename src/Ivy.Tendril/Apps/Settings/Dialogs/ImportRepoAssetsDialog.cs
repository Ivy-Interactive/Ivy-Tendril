using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        var sourceMode = UseState(() => projectRepos.Count > 0 ? "Project Repo" : "Git URL");
        var selectedRepoPath = UseState(() => projectRepos.Count > 0 ? projectRepos[0].Path : "");
        var gitUrl = UseState("");
        var localPath = UseState("");
        var isScanning = UseState(false);
        var scanStatusMessage = UseState<string?>(null);
        var scanErrorMessage = UseState<string?>(null);
        var discoveredMcp = UseState(() => new List<DiscoveredMcpServer>());
        var discoveredSkills = UseState(() => new List<DiscoveredSkill>());
        var selectedItemNames = UseState(() => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        void PerformScan(string rawInput)
        {
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                discoveredMcp.Set(new List<DiscoveredMcpServer>());
                discoveredSkills.Set(new List<DiscoveredSkill>());
                scanErrorMessage.Set("Please specify a repository source.");
                return;
            }

            isScanning.Set(true);
            scanErrorMessage.Set(null);
            scanStatusMessage.Set("Scanning repository...");

            Task.Run(() =>
            {
                try
                {
                    var (resolvedPath, error) = RepoAssetScanner.ResolveAndPrepareRepoPath(rawInput, config.TendrilHome ?? "");
                    if (error != null || string.IsNullOrEmpty(resolvedPath))
                    {
                        scanErrorMessage.Set(error ?? "Failed to access repository.");
                        discoveredMcp.Set(new List<DiscoveredMcpServer>());
                        discoveredSkills.Set(new List<DiscoveredSkill>());
                        selectedItemNames.Set(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                        return;
                    }

                    if (kind == ImportAssetKind.McpServers)
                    {
                        var servers = RepoAssetScanner.ScanMcpServers(resolvedPath);
                        discoveredMcp.Set(servers);
                        selectedItemNames.Set(new HashSet<string>(servers.Select(s => s.Name), StringComparer.OrdinalIgnoreCase));
                    }
                    else
                    {
                        var foundSkills = RepoAssetScanner.ScanSkills(resolvedPath);
                        discoveredSkills.Set(foundSkills);
                        selectedItemNames.Set(new HashSet<string>(foundSkills.Select(s => s.Name), StringComparer.OrdinalIgnoreCase));
                    }
                }
                catch (Exception ex)
                {
                    scanErrorMessage.Set($"Scan failed: {ex.Message}");
                }
                finally
                {
                    isScanning.Set(false);
                    scanStatusMessage.Set(null);
                }
            });
        }

        // Auto-scan on mount or when selected project repo changes
        UseEffect(() =>
        {
            if (!isOpen.Value) return;

            if (sourceMode.Value == "Project Repo" && !string.IsNullOrWhiteSpace(selectedRepoPath.Value))
            {
                PerformScan(selectedRepoPath.Value);
            }
        }, [isOpen, sourceMode, selectedRepoPath]);

        if (!isOpen.Value) return null;

        var availableModes = new List<string>();
        if (projectRepos.Count > 0)
            availableModes.Add("Project Repo");
        availableModes.Add("Git URL");
        availableModes.Add("Local Path");

        var allItemNames = kind == ImportAssetKind.McpServers
            ? discoveredMcp.Value.Select(m => m.Name).ToList()
            : discoveredSkills.Value.Select(s => s.Name).ToList();

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

        void ExecuteImport()
        {
            var selectedSet = selectedItemNames.Value;
            if (selectedSet.Count == 0) return;

            if (kind == ImportAssetKind.McpServers && mcpServers != null)
            {
                var toImport = discoveredMcp.Value.Where(m => selectedSet.Contains(m.Name)).ToList();
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
                var toImport = discoveredSkills.Value.Where(s => selectedSet.Contains(s.Name)).ToList();
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

        var modeToggle = sourceMode.ToSelectInput(availableModes.ToOptions())
            .Variant(SelectInputVariant.Toggle);

        var sourceInputsLayout = Layout.Vertical();

        if (sourceMode.Value == "Project Repo")
        {
            var repoOptions = projectRepos.Select(r => new Option<string>(r.Path, Path.GetFileName(r.Path) ?? r.Path)).ToList();
            sourceInputsLayout |= Text.Block("Project Repository").Bold().Small();
            sourceInputsLayout |= selectedRepoPath.ToSelectInput(repoOptions);
        }
        else if (sourceMode.Value == "Git URL")
        {
            sourceInputsLayout |= Text.Block("Git Repository URL").Bold().Small();
            sourceInputsLayout |= (Layout.Horizontal().AlignContent(Align.Left).Width(Size.Full())
                | gitUrl.ToTextInput("https://github.com/owner/repo.git or git@...").Width(Size.Full())
                | new Button("Fetch & Scan").Icon(Icons.Search).Outline().Loading(isScanning.Value).OnClick(() => PerformScan(gitUrl.Value)));
        }
        else if (sourceMode.Value == "Local Path")
        {
            sourceInputsLayout |= Text.Block("Local Folder Path").Bold().Small();
            sourceInputsLayout |= (Layout.Horizontal().AlignContent(Align.Left).Width(Size.Full())
                | localPath.ToTextInput("~/path/to/repository or /Users/...").Width(Size.Full())
                | new Button("Scan").Icon(Icons.Search).Outline().Loading(isScanning.Value).OnClick(() => PerformScan(localPath.Value)));
        }

        var resultsLayout = Layout.Vertical();

        if (isScanning.Value)
        {
            resultsLayout |= Text.Block(scanStatusMessage.Value ?? "Scanning repository...").Muted().Small();
        }
        else if (!string.IsNullOrEmpty(scanErrorMessage.Value))
        {
            resultsLayout |= new Callout(scanErrorMessage.Value, icon: Icons.TriangleAlert);
        }
        else if (kind == ImportAssetKind.McpServers)
        {
            var serversList = discoveredMcp.Value;
            if (serversList.Count == 0)
            {
                resultsLayout |= Text.Block("No MCP configuration files (.mcp.json, mcp_config.json, .vscode/mcp.json) found.").Muted().Small();
            }
            else
            {
                var selectAllBtn = new Button(selectedItemNames.Value.Count == allItemNames.Count ? "Deselect All" : "Select All")
                    .Ghost().Small().OnClick(ToggleSelectAll);

                resultsLayout |= (Layout.Horizontal().AlignContent(Align.Left)
                    | Text.Block($"Discovered MCP Servers ({serversList.Count})").Bold().Small()
                    | selectAllBtn);

                var itemsList = Layout.Vertical();
                foreach (var srv in serversList)
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
            var skillsList = discoveredSkills.Value;
            if (skillsList.Count == 0)
            {
                resultsLayout |= Text.Block("No SKILL.md files found in this repository (.agents/skills, .gemini/skills, skills/, etc.).").Muted().Small();
            }
            else
            {
                var selectAllBtn = new Button(selectedItemNames.Value.Count == allItemNames.Count ? "Deselect All" : "Select All")
                    .Ghost().Small().OnClick(ToggleSelectAll);

                resultsLayout |= (Layout.Horizontal().AlignContent(Align.Left)
                    | Text.Block($"Discovered Custom Skills ({skillsList.Count})").Bold().Small()
                    | selectAllBtn);

                var itemsList = Layout.Vertical();
                foreach (var sk in skillsList)
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

        var selectedCount = selectedItemNames.Value.Count;

        var body = Layout.Vertical()
            | Text.Block("Repository Source").Bold().Small()
            | modeToggle
            | sourceInputsLayout
            | new Separator()
            | resultsLayout;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(title),
            new DialogBody(body),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button($"Import Selected ({selectedCount})").Primary().Disabled(selectedCount == 0 || isScanning.Value).OnClick(ExecuteImport)
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
