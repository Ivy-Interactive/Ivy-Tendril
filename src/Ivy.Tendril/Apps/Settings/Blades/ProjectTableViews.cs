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

            var rightGroup = Layout.Horizontal().AlignContent(Align.Right).Width(Size.Fit())
                | new Button().Icon(Icons.Copy).Ghost().Tooltip("Copy file path").OnClick(() =>
                {
                    copyToClipboard(fullPath);
                    client.Toast("Copied memory path to clipboard", "Copied");
                })
                | new Button().Icon(Icons.Pencil).Ghost().Tooltip("Edit").OnClick(() => onEdit(fileName))
                | new Button().Icon(Icons.Trash).Ghost().Tooltip("Delete").OnClick(() =>
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
                | new Button("Add Project Memory").Icon(Icons.Plus).OnClick(() => onEdit(null)));

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
                    | (_onEdit != null ? new Button("Add MCP Server").Icon(Icons.Plus).OnClick(() => _onEdit(null)) : null)
                    | (_onImport != null ? new Button("Import from Repository").Icon(Icons.Download).OnClick(_onImport) : null));

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

            var rightGroup = Layout.Horizontal().AlignContent(Align.Right).Width(Size.Fit())
                | new Button().Icon(Icons.Copy).Ghost().Tooltip("Copy command").OnClick(() =>
                {
                    copyToClipboard(fullCmd);
                    client.Toast("Copied command to clipboard", "Copied");
                })
                | (_onEdit != null ? new Button().Icon(Icons.Pencil).Ghost().Tooltip("Edit").OnClick(() => _onEdit(idx)) : null)
                | new Button().Icon(Icons.Trash).Ghost().Tooltip("Delete").OnClick(() =>
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
                | (_onEdit != null ? new Button("Add MCP Server").Icon(Icons.Plus).OnClick(() => _onEdit(null)) : null)
                | (_onImport != null ? new Button("Import from Repository").Icon(Icons.Download).OnClick(_onImport) : null));

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
                    | (_onEdit != null ? new Button("Add Custom Skill").Icon(Icons.Plus).OnClick(() => _onEdit(null)) : null)
                    | (_onImport != null ? new Button("Import from Repository").Icon(Icons.Download).OnClick(_onImport) : null));

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

            var rightGroup = Layout.Horizontal().AlignContent(Align.Right).Width(Size.Fit())
                | new Button().Icon(Icons.Copy).Ghost().Tooltip("Copy skill path").OnClick(() =>
                {
                    if (!string.IsNullOrWhiteSpace(skill.Path))
                    {
                        copyToClipboard(skill.Path);
                        client.Toast("Copied skill path to clipboard", "Copied");
                    }
                })
                | (_onEdit != null ? new Button().Icon(Icons.Pencil).Ghost().Tooltip("Edit").OnClick(() => _onEdit(idx)) : null)
                | new Button().Icon(Icons.Trash).Ghost().Tooltip("Delete").OnClick(() =>
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
                | (_onEdit != null ? new Button("Add Custom Skill").Icon(Icons.Plus).OnClick(() => _onEdit(null)) : null)
                | (_onImport != null ? new Button("Import from Repository").Icon(Icons.Download).OnClick(_onImport) : null));

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
    Action<int?> onEdit) : ViewBase
{
    public override object? Build()
    {
        var actions = reviewActions.Value;
        if (actions.Count == 0) return null;

        var rows = actions.Select((a, i) => new ReviewActionRow(a.Name, i)).ToList();

        return new TableBuilder<ReviewActionRow>(rows)
            .Header(t => t.Name, "Action Name")
            .Builder(t => t.Name, f => f.Func<ReviewActionRow, string>(name =>
                Text.Block(name).Bold()
            ))
            .ColumnWidth(t => t.Name, Size.Grow())
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<ReviewActionRow, int>(idx =>
                Layout.Horizontal().AlignContent(Align.Right)
                | new Button().Icon(Icons.Pencil).Ghost().Tooltip("Edit").OnClick(() => onEdit(idx))
                | new Button().Icon(Icons.Trash).Ghost().Tooltip("Delete").OnClick(() =>
                {
                    var list = new List<ReviewActionConfig>(actions);
                    list.RemoveAt(idx);
                    reviewActions.Set(list);
                })
            ))
            .ColumnWidth(t => t.Index, Size.Fit())
            .Width(Size.Full());
    }

    private record ReviewActionRow(string Name, int Index);
}

public class ProjectVerificationsTableView(
    IState<List<ProjectVerificationRef>> verifications,
    Action<int?> onEdit) : ViewBase
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
            .ColumnWidth(t => t.Name, Size.Grow())
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<VerificationRow, int>(idx =>
                Layout.Horizontal().AlignContent(Align.Right)
                | new Button().Icon(Icons.Pencil).Ghost().Tooltip("Edit").OnClick(() => onEdit(idx))
                | new Button().Icon(Icons.Trash).Ghost().Tooltip("Delete").OnClick(() =>
                {
                    var current = new List<ProjectVerificationRef>(verifications.Value);
                    current.RemoveAt(idx);
                    verifications.Set(current);
                })
            ))
            .ColumnWidth(t => t.Index, Size.Fit())
            .Width(Size.Full());
    }

    private record VerificationRow(string Name, int Index);
}
