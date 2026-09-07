using Ivy.Tendril.Apps.ReviewAction;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Blades;

public class ProjectMemoryTableView(
    string tendrilHome,
    string projectName,
    IState<int> refreshCounter,
    Action<string?> onEdit) : ViewBase
{
    public override object? Build()
    {
        var copyToClipboard = UseClipboard();
        var client = UseService<IClientProvider>();
        _ = refreshCounter.Value;

        var memoryDir = ProjectPathHelper.GetMemoryDir(tendrilHome, projectName);
        var files = Directory.Exists(memoryDir)
            ? Directory.GetFiles(memoryDir, "*.md")
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n!)
                .ToList()
            : new List<string?>();

        var header = Layout.Horizontal().AlignContent(Align.Left)
            | Text.H4("Project Memories").Bold()
            | new Badge($"{files.Count}").Variant(BadgeVariant.Secondary).Small();

        if (files.Count == 0)
        {
            var emptyContent = Layout.Vertical()
                | Text.Block("No project memory files found (stored in .tendril/Projects/<Project>/memory/).").Muted().Small()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | new Button("Add Project Memory").Icon(Icons.Plus).Outline().Small().OnClick(() => onEdit(null)));

            return new Expandable(header, emptyContent).Open(true);
        }

        var cards = Layout.Vertical();
        for (int i = 0; i < files.Count; i++)
        {
            var fileName = files[i]!;
            var idx = i;
            var fullPath = Path.Combine(memoryDir, fileName);

            string? snippet = null;
            try
            {
                if (File.Exists(fullPath))
                {
                    var lines = File.ReadAllLines(fullPath)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Take(2)
                        .Select(l => l.Trim().TrimStart('#', ' ', '-'))
                        .Where(l => !string.IsNullOrWhiteSpace(l));
                    snippet = string.Join(" — ", lines);
                }
            }
            catch { }

            // Header row of Memory card
            var leftGroup = Layout.Horizontal().AlignContent(Align.Left).Width(Size.Fit())
                | Text.Inline(fileName).Bold().Small()
                | new Badge("Memory").Color(Colors.Blue).Variant(BadgeVariant.Secondary).Small();

            var rightGroup = Layout.Horizontal().AlignContent(Align.Right).Width(Size.Fit()).Gap(1)
                | new Button().Icon(Icons.Copy).Outline().Small().Tooltip("Copy file path").OnClick(() =>
                {
                    copyToClipboard(fullPath);
                    client.Toast("Copied memory path to clipboard", "Copied");
                })
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => onEdit(fileName))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                    refreshCounter.Set(refreshCounter.Value + 1);
                });

            var cardHeader = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
                | leftGroup
                | rightGroup;

            var cardBody = Text.Block(string.IsNullOrWhiteSpace(snippet) ? "Memory markdown document." : snippet).Muted().Small();

            var card = Layout.Vertical()
                | cardHeader
                | cardBody;

            cards |= card;
        }

        var scrollableCards = files.Count > 5
            ? (Layout.Vertical().Height(Size.Rem(18)).Scroll(Scroll.Auto) | cards)
            : cards;

        var containerBox = new Box(scrollableCards)
            .BorderRadius(BorderRadius.Rounded)
            .BorderColor(Colors.Slate, 0.2f)
            .Width(Size.Full());

        var content = Layout.Vertical()
            | containerBox
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Button("Add Project Memory").Icon(Icons.Plus).Outline().OnClick(() => onEdit(null)));

        return new Expandable(header, content).Open(true);
    }
}

public class McpServersTableView : ViewBase
{
    private readonly IState<List<ProjectMcpServerRef>> _mcpServers;
    private readonly IState<List<RepoRef>>? _repos;
    private readonly Action<int?>? _onEdit;
    private readonly Action? _onImport;
    private readonly Action<int>? _onDelete;

    public McpServersTableView(IState<List<ProjectMcpServerRef>> mcpServers, Action<int?> onEdit)
    {
        _mcpServers = mcpServers;
        _onEdit = onEdit;
    }

    public McpServersTableView(IState<List<ProjectMcpServerRef>> mcpServers, IState<List<RepoRef>>? repos, Action<int?>? onEdit = null, Action? onImport = null, Action<int>? onDelete = null)
    {
        _mcpServers = mcpServers;
        _repos = repos;
        _onEdit = onEdit;
        _onImport = onImport;
        _onDelete = onDelete;
    }

    public override object? Build()
    {
        var copyToClipboard = UseClipboard();
        var client = UseService<IClientProvider>();
        var list = _mcpServers.Value;

        var header = Layout.Horizontal().AlignContent(Align.Left)
            | Text.H4("MCP Tools & Servers").Bold()
            | new Badge($"{list.Count}").Variant(BadgeVariant.Secondary).Small();

        if (list.Count == 0)
        {
            var emptyContent = Layout.Vertical()
                | Text.Block("No MCP servers configured for this project.").Muted().Small()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | (_onEdit != null ? new Button("Add MCP Server").Icon(Icons.Plus).Outline().OnClick(() => _onEdit(null)) : null)
                    | (_onImport != null ? new Button("Import from Repository").Icon(Icons.Download).Outline().OnClick(_onImport) : null));

            return new Expandable(header, emptyContent).Open(true);
        }

        var cards = Layout.Vertical();
        for (int i = 0; i < list.Count; i++)
        {
            var srv = list[i];
            var idx = i;
            var argsStr = srv.Arguments.Count > 0 ? " " + string.Join(" ", srv.Arguments) : "";
            var fullCmd = $"{srv.Command}{argsStr}";

            // Header row of MCP card
            var leftGroup = Layout.Horizontal().AlignContent(Align.Left).Width(Size.Fit())
                | Text.Inline(srv.Name).Bold().Small()
                | new Badge("MCP").Color(Colors.Green).Variant(BadgeVariant.Secondary).Small();

            var rightGroup = Layout.Horizontal().AlignContent(Align.Right).Width(Size.Fit()).Gap(1)
                | new Button().Icon(Icons.Copy).Outline().Small().Tooltip("Copy command").OnClick(() =>
                {
                    copyToClipboard(fullCmd);
                    client.Toast("Copied command to clipboard", "Copied");
                })
                | (_onEdit != null ? new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => _onEdit(idx)) : null)
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    if (_onDelete != null)
                    {
                        _onDelete(idx);
                    }
                    else
                    {
                        var current = new List<ProjectMcpServerRef>(_mcpServers.Value);
                        current.RemoveAt(idx);
                        _mcpServers.Set(current);
                    }
                });

            var cardHeader = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
                | leftGroup
                | rightGroup;

            // Command / description row
            var cardBody = Text.Block(fullCmd).Muted().Small();

            var card = Layout.Vertical()
                | cardHeader
                | cardBody;

            cards |= card;
        }

        var scrollableCards = list.Count > 5
            ? (Layout.Vertical().Height(Size.Rem(18)).Scroll(Scroll.Auto) | cards)
            : cards;

        var containerBox = new Box(scrollableCards)
            .BorderRadius(BorderRadius.Rounded)
            .BorderColor(Colors.Slate, 0.2f)
            .Width(Size.Full());

        var content = Layout.Vertical()
            | containerBox
            | (Layout.Horizontal().AlignContent(Align.Left)
                | (_onEdit != null ? new Button("Add MCP Server").Icon(Icons.Plus).Outline().OnClick(() => _onEdit(null)) : null)
                | (_onImport != null ? new Button("Import from Repository").Icon(Icons.Download).Outline().OnClick(_onImport) : null));

        return new Expandable(header, content).Open(true);
    }
}

public class SkillsTableView : ViewBase
{
    private readonly IState<List<ProjectSkillRef>> _skills;
    private readonly IState<List<RepoRef>>? _repos;
    private readonly Action<int?>? _onEdit;
    private readonly Action? _onImport;
    private readonly Action<int>? _onDelete;

    public SkillsTableView(IState<List<ProjectSkillRef>> skills, Action<int?> onEdit)
    {
        _skills = skills;
        _onEdit = onEdit;
    }

    public SkillsTableView(IState<List<ProjectSkillRef>> skills, IState<List<RepoRef>>? repos, Action<int?>? onEdit = null, Action? onImport = null, Action<int>? onDelete = null)
    {
        _skills = skills;
        _repos = repos;
        _onEdit = onEdit;
        _onImport = onImport;
        _onDelete = onDelete;
    }

    public override object? Build()
    {
        var copyToClipboard = UseClipboard();
        var client = UseService<IClientProvider>();
        var list = _skills.Value;

        var header = Layout.Horizontal().AlignContent(Align.Left)
            | Text.H4("Custom Skills").Bold()
            | new Badge($"{list.Count}").Variant(BadgeVariant.Secondary).Small();

        if (list.Count == 0)
        {
            var emptyContent = Layout.Vertical()
                | Text.Block("No custom skills configured for this project.").Muted().Small()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | (_onEdit != null ? new Button("Add Custom Skill").Icon(Icons.Plus).Outline().OnClick(() => _onEdit(null)) : null)
                    | (_onImport != null ? new Button("Import from Repository").Icon(Icons.Download).Outline().OnClick(_onImport) : null));

            return new Expandable(header, emptyContent).Open(true);
        }

        var cards = Layout.Vertical();
        for (int i = 0; i < list.Count; i++)
        {
            var skill = list[i];
            var idx = i;
            var path = skill.Path ?? "";

            // Header row of skill card
            var leftGroup = Layout.Horizontal().AlignContent(Align.Left).Width(Size.Fit())
                | Text.Inline(skill.Name).Bold().Small();

            // Check repo match
            var matchingRepo = _repos?.Value.FirstOrDefault(r => !string.IsNullOrEmpty(r.Path) && path.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase));
            if (matchingRepo != null)
            {
                var repoName = Path.GetFileName(matchingRepo.Path.TrimEnd('/', '\\')) ?? matchingRepo.Path;
                leftGroup |= new Badge($"Repo: {repoName}").Color(Colors.Purple).Variant(BadgeVariant.Secondary).Small();
            }
            else if (path.Contains("/plugins/", StringComparison.OrdinalIgnoreCase) || path.Contains("/plugin/", StringComparison.OrdinalIgnoreCase))
            {
                var pluginName = ExtractPluginName(path);
                if (!string.IsNullOrEmpty(pluginName))
                    leftGroup |= new Badge($"Plugin: {pluginName}").Color(Colors.Purple).Variant(BadgeVariant.Secondary).Small();
            }
            else if (path.Contains(".gemini", StringComparison.OrdinalIgnoreCase) || path.Contains("Global", StringComparison.OrdinalIgnoreCase))
            {
                leftGroup |= new Badge("Global").Color(Colors.Blue).Variant(BadgeVariant.Secondary).Small();
            }
            else
            {
                leftGroup |= new Badge("Project").Color(Colors.Blue).Variant(BadgeVariant.Secondary).Small();
            }

            var rightGroup = Layout.Horizontal().AlignContent(Align.Right).Width(Size.Fit()).Gap(1)
                | new Button().Icon(Icons.Copy).Outline().Small().Tooltip("Copy skill path").OnClick(() =>
                {
                    if (!string.IsNullOrWhiteSpace(skill.Path))
                    {
                        copyToClipboard(skill.Path);
                        client.Toast("Copied skill path to clipboard", "Copied");
                    }
                })
                | (_onEdit != null ? new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => _onEdit(idx)) : null)
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    if (_onDelete != null)
                    {
                        _onDelete(idx);
                    }
                    else
                    {
                        var current = new List<ProjectSkillRef>(_skills.Value);
                        current.RemoveAt(idx);
                        _skills.Set(current);
                    }
                });

            var cardHeader = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
                | leftGroup
                | rightGroup;

            // Description row
            var cardBody = Text.Block(string.IsNullOrWhiteSpace(skill.Description) ? "No description provided." : skill.Description).Muted().Small();

            var card = Layout.Vertical()
                | cardHeader
                | cardBody;

            cards |= card;
        }

        var scrollableCards = list.Count > 5
            ? (Layout.Vertical().Height(Size.Rem(18)).Scroll(Scroll.Auto) | cards)
            : cards;

        var containerBox = new Box(scrollableCards)
            .BorderRadius(BorderRadius.Rounded)
            .BorderColor(Colors.Slate, 0.2f)
            .Width(Size.Full());

        var content = Layout.Vertical()
            | containerBox
            | (Layout.Horizontal().AlignContent(Align.Left)
                | (_onEdit != null ? new Button("Add Custom Skill").Icon(Icons.Plus).Outline().OnClick(() => _onEdit(null)) : null)
                | (_onImport != null ? new Button("Import from Repository").Icon(Icons.Download).Outline().OnClick(_onImport) : null));

        return new Expandable(header, content).Open(true);
    }

    private static string? ExtractPluginName(string path)
    {
        var idx = path.IndexOf("/plugins/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var remainder = path[(idx + "/plugins/".Length)..];
            var slashIdx = remainder.IndexOf('/');
            return slashIdx > 0 ? remainder[..slashIdx] : remainder;
        }
        return null;
    }
}

public class ReviewActionsTableView(
    IState<List<ReviewActionConfig>> reviewActions,
    Action<int?> onEdit,
    string? projectName = null,
    Action<ReviewActionConfig>? onRun = null,
    bool showRun = true) : ViewBase
{
    public override object? Build()
    {
        var nav = UseNavigation();
        var actions = reviewActions.Value;
        if (actions.Count == 0) return null;

        var rows = actions.Select((a, i) => new ReviewActionRow(a.Name, i)).ToList();

        return new TableBuilder<ReviewActionRow>(rows)
            .Header(t => t.Name, "Action Name")
            .Builder(t => t.Name, f => f.Func<ReviewActionRow, string>(name =>
                Text.Block(name).Bold()
            ))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<ReviewActionRow, int>(idx =>
            {
                var action = actions[idx];
                var rowLayout = Layout.Horizontal().Gap(1);
                if (showRun)
                {
                    rowLayout = rowLayout | new Button().Icon(Icons.Play).Outline().Small().Tooltip("Run / Preview Action").OnClick(() =>
                    {
                        if (onRun != null)
                        {
                            onRun(action);
                        }
                        else
                        {
                            nav.Navigate<ReviewActionApp>(new ReviewActionAppArgs(ActionName: action.Name, ProjectName: projectName));
                        }
                    });
                }
                return rowLayout
                    | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => onEdit(idx))
                    | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                    {
                        var list = new List<ReviewActionConfig>(reviewActions.Value);
                        list.RemoveAt(idx);
                        reviewActions.Set(list);
                    });
            }))
            .Width(Size.Fit());
    }

    private record ReviewActionRow(string Name, int Index);
}

/// <summary>
///     The project's named service ports. Each row's default port is what a plan worktree tries first;
///     the port actually assigned to a plan is shown on its review actions bar.
/// </summary>
public class ProjectPortsTableView(
    IState<Dictionary<string, ProjectPortConfig>> ports,
    Action<string?> onEdit) : ViewBase
{
    public override object? Build()
    {
        var current = ports.Value;
        if (current.Count == 0) return null;

        var rows = current.Select((p, i) => new PortRow(p.Key, p.Value.DefaultPort, p.Value.Description, i)).ToList();

        return new TableBuilder<PortRow>(rows)
            .Header(t => t.Name, "Name")
            .Builder(t => t.Name, f => f.Func<PortRow, string>(name =>
                Text.Block(name).Bold()
            ))
            .Header(t => t.DefaultPort, "Default Port")
            .Header(t => t.Description, "Description")
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<PortRow, int>(idx =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => onEdit(rows[idx].Name))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    var updated = new Dictionary<string, ProjectPortConfig>(ports.Value);
                    updated.Remove(rows[idx].Name);
                    ports.Set(updated);
                })
            ))
            .Width(Size.Fit());
    }

    private record PortRow(string Name, int DefaultPort, string Description, int Index);
}

/// <summary>
///     The environment files recreated inside every plan worktree, since a fresh worktree has none of
///     the untracked <c>.env</c> files the original checkout relies on.
/// </summary>
public class ProjectEnvFilesTableView(
    IState<List<ProjectEnvFileConfig>> envFiles,
    Action<int?> onEdit) : ViewBase
{
    public override object? Build()
    {
        var list = envFiles.Value;
        if (list.Count == 0) return null;

        var rows = list
            .Select((f, i) => new EnvFileRow(f.Path, f.Template ?? "", string.Join(", ", f.Overrides.Keys), i))
            .ToList();

        return new TableBuilder<EnvFileRow>(rows)
            .Header(t => t.Path, "Path")
            .Builder(t => t.Path, f => f.Func<EnvFileRow, string>(path =>
                Text.Block(path).Bold()
            ))
            .Header(t => t.Template, "Template")
            .Header(t => t.Overrides, "Overrides")
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<EnvFileRow, int>(idx =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => onEdit(idx))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    var updated = new List<ProjectEnvFileConfig>(envFiles.Value);
                    updated.RemoveAt(idx);
                    envFiles.Set(updated);
                })
            ))
            .Width(Size.Fit());
    }

    private record EnvFileRow(string Path, string Template, string Overrides, int Index);
}

public class ProjectVerificationsTableView(
    IState<List<ProjectVerificationRef>> verifications,
    Action<string?> onEdit) : ViewBase
{
    public override object? Build()
    {
        var list = verifications.Value;
        if (list.Count == 0) return null;

        var rows = list.Select((v, i) => new VerificationRow(v.Name, i)).ToList();

        return new TableBuilder<VerificationRow>(rows)
            .Header(t => t.Name, "Verification Name")
            .Builder(t => t.Name, f => f.Func<VerificationRow, string>(name =>
                Text.Block(name).Bold()
            ))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<VerificationRow, int>(idx =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => onEdit(rows[idx].Name))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    var current = new List<ProjectVerificationRef>(verifications.Value);
                    current.RemoveAt(idx);
                    verifications.Set(current);
                })
            ))
            .Width(Size.Fit());
    }

    private record VerificationRow(string Name, int Index);
}
