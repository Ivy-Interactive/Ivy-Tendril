using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using System.Collections.Generic;
using System.Linq;

namespace Ivy.Tendril.Apps.Library;

public class SidebarView(
    List<string> files,
    IState<string?> selectedNote,
    IState<string?> searchQuery,
    IState<bool> isNewNoteOpen,
    VaultStatusInfo? status) : ViewBase
{
    public override object Build()
    {
        var filteredList = files;
        if (!string.IsNullOrWhiteSpace(searchQuery.Value))
        {
            var q = searchQuery.Value.ToLowerInvariant();
            filteredList = files.Where(f => f.ToLowerInvariant().Contains(q)).ToList();
        }

        var sidebarHeader = Layout.Vertical()
            | searchQuery.ToSearchInput()
                .Placeholder("Search memories...")
                .Suffix(
                    new Button()
                        .Icon(Icons.Plus)
                        .Ghost()
                        .OnClick(() => isNewNoteOpen.Set(true))
                );

        object sidebarContent;
        if (filteredList.Count == 0)
        {
            sidebarContent = Layout.Center() | Text.Muted("No memories found");
        }
        else
        {
            sidebarContent = new List(filteredList.Select(f =>
            {
                var item = f;
                var noteIsOutdated = status != null && status.OutdatedNoteNames.Contains(item);
                var rowBadges = Layout.Horizontal()
                    | (noteIsOutdated ? (object)new Badge("Outdated").Variant(BadgeVariant.Warning).Small() : new Fragment());

                return SidebarListRow.Build(
                    item, 
                    rowBadges, 
                    () => selectedNote.Set(item)
                );
            }));
        }

        return new HeaderLayout(sidebarHeader, sidebarContent);
    }
}
