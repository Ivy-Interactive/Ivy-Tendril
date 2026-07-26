using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Widgets;
using Ivy.Tendril.Apps.Library.Dialogs;

namespace Ivy.Tendril.Apps.Library;

public class ContentView(
    IState<string?> selectedNote,
    VaultStatusInfo? status,
    IState<bool> isLoading,
    IState<bool> isEditing,
    IState<string> editContent,
    IState<bool> isOperationRunning,
    IState<string> operationLogs,
    string? vaultPath,
    string? memoriesDir,
    string? defaultVaultDir,
    string[]? ignorePatterns,
    Action onLoadStatus,
    Action<string, string> onRunCommand,
    Action onSyncAllHashes,
    IClientProvider client,
    IState<bool> isDeleteOpen,
    object headerView,
    string workingDir,
    IState<bool> isUpdateMemoriesOpen,
    Action onLoadFiles,
    IState<bool> isGraphView,
    List<string> files,
    Action<string, string> onStartAiEditJob) : ViewBase
{
    public override object Build()
    {
        var isAiEditOpen = UseState(false);

        // 1. Vault not initialized
        if (vaultPath == null)
        {
            var initContent = Layout.Vertical()
                   | headerView
                   | new Spacer().Height(Size.Units(5))
                   | new Card(
                       Layout.Vertical().AlignContent(Align.Center)
                       | Icons.Folder.ToIcon().Size(Size.Units(12)).Color(Colors.Warning)
                       | Text.H3("No Knowledge Vault Initialized").Bold()
                       | Text.Muted("An Obsidian-style vault (Promptwares) is required to track codebase memory notes and code/variant reference hashes in this project.")
                       | (isOperationRunning.Value
                          ? (object)(Layout.Vertical().AlignContent(Align.Center)
                            | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                            | Text.Muted("Initializing vault..."))
                          : new Button("Initialize Vault")
                              .Primary()
                              .Icon(Icons.FolderPlus)
                              .OnClick(() => onRunCommand("init", "init")))
                     );

            if (!string.IsNullOrEmpty(operationLogs.Value))
            {
                initContent = initContent
                    | new Card(
                        Layout.Vertical()
                        | Text.H3("Initialization Output").Bold()
                        | Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(150))
                            | Text.Block(operationLogs.Value).Small()
                      );
            }

            return initContent;
        }

        // 2. Vault initialized
        var isClean = status != null && status.OutdatedMemories == 0 && status.BrokenWikiLinks == 0;

        if (selectedNote.Value != null)
        {
            // Note details panel
            var noteName = selectedNote.Value;
            var noteFile = Path.Combine(memoriesDir!, noteName + ".md");
            var isOutdated = status != null && status.OutdatedNoteNames.Contains(noteName);

            var noteHeader = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.Center)
                | new Button("Back").Icon(Icons.ArrowLeft).Outline().OnClick(() => selectedNote.Set(null))
                | Text.H2(noteName).Bold()
                | (isOutdated ? (object)new Badge("Hashes Outdated").Variant(BadgeVariant.Warning).Small() : new Fragment());

            var noteActionBar = Layout.Horizontal().AlignContent(Align.Center)
                | (isEditing.Value
                   ? new Fragment(
                       new Button("Save").Icon(Icons.Save).Primary().OnClick(() =>
                       {
                           try
                           {
                               File.WriteAllText(noteFile, editContent.Value);
                               isEditing.Set(false);
                               client.Toast("Memory note saved.", "Saved");
                               onLoadStatus();
                           }
                           catch (Exception ex)
                           {
                               client.Toast($"Failed to save: {ex.Message}", "Error");
                           }
                       }),
                       new Button("Cancel").Outline().OnClick(() =>
                       {
                           if (File.Exists(noteFile))
                               editContent.Set(File.ReadAllText(noteFile));
                           isEditing.Set(false);
                       })
                     )
                   : new Fragment(
                       new Button("AI Edit").Icon(Icons.Sparkles).Outline().OnClick(() => isAiEditOpen.Set(true)),
                       new Button("Edit").Icon(Icons.Pencil).Outline().OnClick(() => isEditing.Set(true)),
                       isOutdated
                         ? (object)new Button("Sync Hashes").Icon(Icons.RefreshCw).Primary().OnClick(() => onRunCommand("update", $"update {noteName}"))
                         : new Fragment(),
                       new Button("Delete").Icon(Icons.Trash).Variant(ButtonVariant.Destructive).OnClick(() => isDeleteOpen.Set(true))
                     ));

            object noteBody;
            if (isEditing.Value)
            {
                noteBody = editContent.ToTextareaInput("Write your markdown memory here...")
                           .Rows(25)
                           .Width(Size.Full())
                           .WithField();
            }
            else
            {
                var markdownText = File.Exists(noteFile) ? File.ReadAllText(noteFile) : "";
                noteBody = Layout.Vertical().Width(Size.Full())
                           | new Markdown(markdownText).Article();
            }

            var detailLayout = new HeaderLayout(
                noteHeader,
                new FooterLayout(
                    noteActionBar,
                    Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full()) | noteBody
                ).Size(Size.Full())
            ).Scroll(Scroll.None).Size(Size.Full());

            if (isAiEditOpen.Value)
            {
                return new Fragment(
                    detailLayout,
                    new AiEditMemoryDialog(isAiEditOpen, noteName, onStartAiEditJob, client)
                );
            }

            return detailLayout;
        }

        // Build Graph Data for ECharts BrainMap
        var graphNodes = new List<BrainNode>();
        var graphEdges = new List<BrainEdge>();
        var noteIds = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        if (memoriesDir != null && Directory.Exists(memoriesDir) && status != null)
        {
            // Parse outdated/missing files from raw status output
            var outdatedFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var missingFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var currentNote = "";

            var rawLines = status.RawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in rawLines)
            {
                if (line.StartsWith("Memory:"))
                {
                    currentNote = line.Substring("Memory:".Length).Trim();
                }
                else if (line.Contains("[OUTDATED CODE]"))
                {
                    var file = line.Substring(line.IndexOf("] ") + 2).Trim();
                    if (file.Contains(" (stored: "))
                    {
                        file = file.Substring(0, file.IndexOf(" (stored: ")).Trim();
                    }
                    if (!string.IsNullOrEmpty(currentNote))
                    {
                        if (!outdatedFiles.TryGetValue(currentNote, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            outdatedFiles[currentNote] = set;
                        }
                        set.Add(file);
                    }
                }
                else if (line.Contains("[MISSING CODE]"))
                {
                    var file = line.Substring(line.IndexOf("] ") + 2).Trim();
                    if (!string.IsNullOrEmpty(currentNote))
                    {
                        if (!missingFiles.TryGetValue(currentNote, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            missingFiles[currentNote] = set;
                        }
                        set.Add(file);
                    }
                }
            }

            foreach (var noteName in files)
            {
                var noteFile = Path.Combine(memoriesDir, noteName + ".md");
                if (!File.Exists(noteFile)) continue;

                var text = File.ReadAllText(noteFile);
                var title = noteName;
                var references = new List<string>();
                var relations = new List<string>();

                var frontmatterYaml = "";
                var firstDash = text.IndexOf("---");
                if (firstDash == 0 || (firstDash > 0 && string.IsNullOrWhiteSpace(text[..firstDash])))
                {
                    var secondDash = text.IndexOf("---", firstDash + 3);
                    if (secondDash > 0)
                    {
                        frontmatterYaml = text.Substring(firstDash + 3, secondDash - (firstDash + 3));
                    }
                }

                if (!string.IsNullOrEmpty(frontmatterYaml))
                {
                    try
                    {
                        var fm = YamlHelper.Deserializer.Deserialize<MemoryFrontmatterDto>(frontmatterYaml);
                        if (fm != null)
                        {
                            if (!string.IsNullOrEmpty(fm.Title))
                            {
                                title = fm.Title;
                            }
                            if (fm.References != null)
                            {
                                foreach (var r in fm.References)
                                {
                                    if (!string.IsNullOrEmpty(r.Path))
                                    {
                                        references.Add(r.Path);
                                    }
                                }
                            }
                            if (fm.Relations != null)
                            {
                                foreach (var rel in fm.Relations)
                                {
                                    if (!string.IsNullOrEmpty(rel))
                                    {
                                        relations.Add(rel);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Fallback: parse lines manually if YAML deserialization fails
                        var lines = text.Split('\n');
                        var inFrontmatter = false;
                        for (int i = 0; i < Math.Min(lines.Length, 30); i++)
                        {
                            var line = lines[i].Trim();
                            if (line == "---")
                            {
                                inFrontmatter = !inFrontmatter;
                                continue;
                            }
                            if (inFrontmatter)
                            {
                                if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                                {
                                    title = line.Substring("title:".Length).Trim(' ', '"', '\'');
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Fallback: parse lines manually if no frontmatter delimiters found
                    var lines = text.Split('\n');
                    var inFrontmatter = false;
                    for (int i = 0; i < Math.Min(lines.Length, 30); i++)
                    {
                        var line = lines[i].Trim();
                        if (line == "---")
                        {
                            inFrontmatter = !inFrontmatter;
                            continue;
                        }
                        if (inFrontmatter)
                        {
                            if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                            {
                                title = line.Substring("title:".Length).Trim(' ', '"', '\'');
                            }
                        }
                    }
                }

                var noteStatus = "ok";
                if (status.OutdatedNoteNames.Contains(noteName))
                {
                    noteStatus = "outdated";
                }

                graphNodes.Add(new BrainNode(noteName, title, "memory", noteStatus));

                foreach (var r in references)
                {
                    if (string.IsNullOrEmpty(r)) continue;
                    var fileId = "file:" + r;
                    var fileName = Path.GetFileName(r);

                    var fileStatus = "ok";
                    if (outdatedFiles.TryGetValue(noteName, out var outSet) && outSet.Contains(r))
                    {
                        fileStatus = "outdated";
                    }
                    else if (missingFiles.TryGetValue(noteName, out var missSet) && missSet.Contains(r))
                    {
                        fileStatus = "broken";
                    }

                    if (!graphNodes.Any(n => n.Id == fileId))
                    {
                        graphNodes.Add(new BrainNode(fileId, fileName, "code", fileStatus));
                    }

                    if (!graphEdges.Any(e => e.Source == noteName && e.Target == fileId))
                    {
                        graphEdges.Add(new BrainEdge(noteName, fileId));
                    }
                }

                foreach (var rel in relations)
                {
                    var target = rel.Trim();
                    if (string.IsNullOrEmpty(target)) continue;

                    var matchedNoteId = noteIds.FirstOrDefault(id => 
                        id.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                        id.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase) ||
                        id.EndsWith("\\" + target, StringComparison.OrdinalIgnoreCase)
                    );

                    if (matchedNoteId != null)
                    {
                        if (!graphEdges.Any(e => e.Source == noteName && e.Target == matchedNoteId))
                        {
                            graphEdges.Add(new BrainEdge(noteName, matchedNoteId));
                        }
                    }
                    else
                    {
                        var brokenId = "broken:" + target;
                        if (!graphNodes.Any(n => n.Id == brokenId))
                        {
                            graphNodes.Add(new BrainNode(brokenId, target, "memory", "broken"));
                        }
                        if (!graphEdges.Any(e => e.Source == noteName && e.Target == brokenId))
                        {
                            graphEdges.Add(new BrainEdge(noteName, brokenId));
                        }
                    }
                }

                var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]");
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    var target = m.Groups[1].Value.Trim();
                    var matchedNoteId = noteIds.FirstOrDefault(id => 
                        id.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                        id.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase) ||
                        id.EndsWith("\\" + target, StringComparison.OrdinalIgnoreCase)
                    );

                    if (matchedNoteId != null)
                    {
                        if (!graphEdges.Any(e => e.Source == noteName && e.Target == matchedNoteId))
                        {
                            graphEdges.Add(new BrainEdge(noteName, matchedNoteId));
                        }
                    }
                    else
                    {
                        var brokenId = "broken:" + target;
                        if (!graphNodes.Any(n => n.Id == brokenId))
                        {
                            graphNodes.Add(new BrainNode(brokenId, target, "memory", "broken"));
                        }
                        if (!graphEdges.Any(e => e.Source == noteName && e.Target == brokenId))
                        {
                            graphEdges.Add(new BrainEdge(noteName, brokenId));
                        }
                    }
                }
            }
        }

        // Stats grid
        object statsGrid;
        if (isLoading.Value)
        {
            statsGrid = Layout.Center()
                        | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                        | Text.Muted("Scanning vault status...");
        }
        else if (status == null)
        {
            statsGrid = Text.Danger("Failed to read vault status.");
        }
        else
        {
            statsGrid = Layout.Horizontal().Wrap(true)
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.TotalMemories.ToString()).Bold()
                      | Text.Muted("Total Memories").Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.OutdatedMemories.ToString()).Bold()
                      | (status.OutdatedMemories > 0 ? Text.Danger("Outdated").Bold() : Text.Success("Up to date")).Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.BrokenWikiLinks.ToString()).Bold()
                      | (status.BrokenWikiLinks > 0 ? Text.Danger("Broken Links").Bold() : Text.Success("Clean Links")).Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.OrphanMemories.ToString()).Bold()
                      | Text.Muted("Orphans").Small()
                  ).Width(Size.Units(40));
        }

        // Unified Actions Toolbar in Header
        var actionsToolbar = Layout.Horizontal().Wrap(true).Gap(2)
            | new Button("Refresh").Outline().Icon(Icons.RefreshCw).OnClick(onLoadStatus)
            | new Button("Clean").Outline().Icon(Icons.Trash2).OnClick(() => onRunCommand("shake", "shake"))
            | new Button("Diagnostics").Outline().Icon(Icons.CircleCheck).OnClick(() => onRunCommand("doctor", "doctor"))
            | new Button("Bootstrap").Outline().Icon(Icons.FolderSync).OnClick(() => onRunCommand("index", "index"))
            | new Button("Agentic Update").Outline().Icon(Icons.Sparkles).OnClick(() =>
              {
                  onLoadFiles();
                  isUpdateMemoriesOpen.Set(true);
              })
            | (status != null && status.OutdatedMemories > 0
               ? (object)new Button("Sync Hashes").Primary().Icon(Icons.RefreshCw).OnClick(onSyncAllHashes)
               : new Fragment());

        // Header with switch toggle and actions toolbar
        var headerRow = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
            | (Layout.Horizontal().AlignContent(Align.Center) | isGraphView.ToSwitchInput(label: "Brain Map"))
            | actionsToolbar;

        var configContent = Layout.Vertical()
            | Text.H2("Configuration").Bold()
            | (Layout.Vertical()
               | (Layout.Horizontal()
                  | Text.Bold("Vault Directory:")
                  | Text.Muted(vaultPath))
               | (Layout.Horizontal()
                  | Text.Bold("Default Directory Key:")
                  | Text.Muted(defaultVaultDir ?? "Promptwares"))
               | (ignorePatterns != null && ignorePatterns.Length > 0
                  ? Layout.Vertical()
                    | Text.Bold("Ignore Patterns:")
                    | Layout.Horizontal().Wrap(true)
                      | new Fragment(ignorePatterns.Select(p => new Badge(p).Variant(BadgeVariant.Secondary).Small() as object).ToArray())
                  : new Fragment()));

        if (isGraphView.Value)
        {
            var brainMapWidget = new BrainMap()
                .Nodes(graphNodes)
                .Edges(graphEdges)
                .SelectedNodeId(null)
                .OnNodeClick(nodeId =>
                {
                    if (noteIds.Contains(nodeId))
                    {
                        selectedNote.Set(nodeId);
                    }
                    else if (nodeId.StartsWith("broken:"))
                    {
                        var cleanTargetName = nodeId.Substring("broken:".Length);
                        client.Toast($"Broken wiki-link: [[{cleanTargetName}]] does not exist.", "Broken Link");
                    }
                    else
                    {
                        client.Toast($"Selected node: {nodeId}", "Graph Node");
                    }
                });

            return new HeaderLayout(
                headerRow,
                Layout.Vertical().Size(Size.Full()) | brainMapWidget
            ).Scroll(Scroll.None).Size(Size.Full());
        }

        var mainBody = Layout.Vertical()
            | (Layout.Vertical()
                | (Layout.Horizontal().AlignContent(Align.Center)
                   | Text.H2("Vault Status").Bold()
                   | (isClean
                      ? new Badge("Vault Verified").Variant(BadgeVariant.Success).Small()
                      : new Badge("Attention Required").Variant(BadgeVariant.Destructive).Small()))
                | statsGrid)
            | (isOperationRunning.Value 
               ? (object)(Layout.Horizontal().AlignContent(Align.Center)
                 | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                 | Text.Muted("Running operation in background..."))
               : new Fragment())
            | new Card(
                  Layout.Vertical()
                  | Text.H3("Command Output / Console Log").Bold()
                  | Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(200))
                      | Text.Block(string.IsNullOrEmpty(operationLogs.Value) ? "No logs yet. Run an action to see terminal output." : operationLogs.Value).Small()
              )
            | new Separator()
            | configContent;

        return new HeaderLayout(
            headerRow,
            Layout.Vertical().Scroll(Scroll.Auto).Size(Size.Full()) | mainBody
        ).Scroll(Scroll.None).Size(Size.Full());
    }
}

public class CodeReferenceDto
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
}

public class MemoryFrontmatterDto
{
    public string Title { get; set; } = "";
    public List<CodeReferenceDto>? References { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Relations { get; set; }
    public string? Type { get; set; }
}
