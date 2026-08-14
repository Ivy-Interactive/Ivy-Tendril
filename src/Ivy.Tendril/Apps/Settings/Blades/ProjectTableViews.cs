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
        _ = refreshCounter.Value;
        var memoryDir = ProjectPathHelper.GetMemoryDir(tendrilHome, projectName);
        if (!Directory.Exists(memoryDir)) return null;

        var files = Directory.GetFiles(memoryDir, "*.md")
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n!)
            .ToList();

        if (files.Count == 0) return null;

        var rows = files.Select((f, i) => new MemoryRow(f!, i)).ToList();

        return new TableBuilder<MemoryRow>(rows)
            .Header(t => t.Name, "Memory File")
            .Builder(t => t.Name, f => f.Func<MemoryRow, string>(name =>
                Text.Block(name).Bold().Small()
            ))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<MemoryRow, int>(idx =>
                Layout.Horizontal()
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit Memory").OnClick(() => onEdit(files[idx]))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete Memory").OnClick(() =>
                {
                    var name = files[idx];
                    var fullPath = Path.Combine(memoryDir, name!);
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                    refreshCounter.Set(refreshCounter.Value + 1);
                })
            ))
            .Width(Size.Fit());
    }

    private record MemoryRow(string Name, int Index);
}

public class McpServersTableView(
    IState<List<ProjectMcpServerRef>> mcpServers,
    Action<int?> onEdit) : ViewBase
{
    public override object? Build()
    {
        var servers = mcpServers.Value;
        if (servers.Count == 0) return null;

        var rows = servers.Select((s, i) => new McpServerRow(s.Name, s.Command, s.Arguments.Count, i)).ToList();

        return new TableBuilder<McpServerRow>(rows)
            .Header(t => t.Name, "Name")
            .Builder(t => t.Name, f => f.Func<McpServerRow, string>(name =>
                Text.Block(name).Bold().Small()
            ))
            .Header(t => t.Command, "Command")
            .Builder(t => t.Command, f => f.Func<McpServerRow, string>(cmd =>
                Text.Block(cmd).Muted().Small()
            ))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<McpServerRow, int>(idx =>
                Layout.Horizontal()
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => onEdit(idx))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    var list = new List<ProjectMcpServerRef>(mcpServers.Value);
                    list.RemoveAt(idx);
                    mcpServers.Set(list);
                })
            ))
            .Width(Size.Fit());
    }

    private record McpServerRow(string Name, string Command, int ArgCount, int Index);
}

public class SkillsTableView(
    IState<List<ProjectSkillRef>> skills,
    Action<int?> onEdit) : ViewBase
{
    public override object? Build()
    {
        var list = skills.Value;
        if (list.Count == 0) return null;

        var rows = list.Select((s, i) => new SkillRow(s.Name, s.Description, i)).ToList();

        return new TableBuilder<SkillRow>(rows)
            .Header(t => t.Name, "Name")
            .Builder(t => t.Name, f => f.Func<SkillRow, string>(name =>
                Text.Block(name).Bold().Small()
            ))
            .Header(t => t.Description, "Description")
            .Builder(t => t.Description, f => f.Func<SkillRow, string>(desc =>
                Text.Block(desc).Muted().Small()
            ))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<SkillRow, int>(idx =>
                Layout.Horizontal()
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => onEdit(idx))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    var current = new List<ProjectSkillRef>(skills.Value);
                    current.RemoveAt(idx);
                    skills.Set(current);
                })
            ))
            .Width(Size.Fit());
    }

    private record SkillRow(string Name, string Description, int Index);
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
                Text.Block(name).Bold().Small()
            ))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<ReviewActionRow, int>(idx =>
                Layout.Horizontal()
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => onEdit(idx))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    var list = new List<ReviewActionConfig>(actions);
                    list.RemoveAt(idx);
                    reviewActions.Set(list);
                })
            ))
            .Width(Size.Fit());
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

        var rows = list.Select((v, i) => new VerificationRow(v.Name, v.Required, i)).ToList();

        return new TableBuilder<VerificationRow>(rows)
            .Header(t => t.Name, "Verification Name")
            .Builder(t => t.Name, f => f.Func<VerificationRow, string>(name =>
                Text.Block(name).Bold().Small()
            ))
            .Header(t => t.Required, "Requirement")
            .Builder(t => t.Required, f => f.Func<VerificationRow, bool>(req =>
                req
                    ? new Badge("Required").Variant(BadgeVariant.Secondary).Small()
                    : new Badge("Optional").Variant(BadgeVariant.Outline).Small()
            ))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<VerificationRow, int>(idx =>
                Layout.Horizontal()
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => onEdit(idx))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    var current = new List<ProjectVerificationRef>(verifications.Value);
                    current.RemoveAt(idx);
                    verifications.Set(current);
                })
            ))
            .Width(Size.Fit());
    }

    private record VerificationRow(string Name, bool Required, int Index);
}
