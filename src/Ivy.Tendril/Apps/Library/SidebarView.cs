using System.Collections.Generic;
using System.Linq;
using Ivy;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Library;

public class SidebarView : ViewBase
{
    private readonly List<string> _files;
    private readonly IState<string?> _selectedNote;
    private readonly IState<string> _searchQuery;
    private readonly IState<string?> _projectFilter;
    private readonly IState<bool> _isNewNoteOpen;
    private readonly VaultStatusInfo? _status;

    public SidebarView(
        List<string> files,
        IState<string?> selectedNote,
        IState<string> searchQuery,
        IState<string?> projectFilter,
        IState<bool> isNewNoteOpen,
        VaultStatusInfo? status)
    {
        _files = files;
        _selectedNote = selectedNote;
        _searchQuery = searchQuery;
        _projectFilter = projectFilter;
        _isNewNoteOpen = isNewNoteOpen;
        _status = status;
    }

    public override object Build()
    {
        var query = _searchQuery.Value.Trim();
        var filteredFiles = _files;

        if (!string.IsNullOrEmpty(query))
        {
            filteredFiles = _files
                .Where(f => f.Contains(query, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var searchInput = _searchQuery.ToTextInput(placeholder: "Search notes...")
            .Suffix(new Button("").Ghost().Icon(Icons.Plus).OnClick(() => _isNewNoteOpen.Set(true)));

        var rows = new List<object>
        {
            SidebarListRow.Build("Dashboard Overview", Icons.LayoutDashboard, () => _selectedNote.Set(null), _selectedNote.Value == null)
        };

        foreach (var file in filteredFiles)
        {
            var isSelected = _selectedNote.Value == file;
            var isOutdated = _status?.OutdatedNoteNames.Contains(file) ?? false;
            var icon = isOutdated ? Icons.TriangleAlert : Icons.FileText;

            rows.Add(SidebarListRow.Build(file, icon, () => _selectedNote.Set(file), isSelected));
        }

        return Layout.Vertical()
            | searchInput
            | Layout.Vertical(rows).Gap(1);
    }
}
