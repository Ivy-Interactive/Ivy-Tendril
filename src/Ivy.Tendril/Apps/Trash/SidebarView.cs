using Ivy.Tendril.Apps.Views;

namespace Ivy.Tendril.Apps.Trash;

public class SidebarView(
    List<TrashFileInfo> files,
    IState<string?> selectedFile,
    IState<string?> searchFilter) : ViewBase
{
    private object BuildHeader()
    {
        return Layout.Vertical().Height(Size.Px(40)).AlignContent(Align.Center)
            | searchFilter.ToSearchInput().Placeholder("Search");
    }

    public override object Build()
    {
        var filteredFiles = files.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(searchFilter.Value))
        {
            var searchTerm = searchFilter.Value.ToLowerInvariant();
            filteredFiles = filteredFiles.Where(f =>
                f.FileName.ToLowerInvariant().Contains(searchTerm) ||
                f.OriginalRequest.ToLowerInvariant().Contains(searchTerm) ||
                f.Project.ToLowerInvariant().Contains(searchTerm) ||
                f.DuplicateOf.ToLowerInvariant().Contains(searchTerm)
            );
        }

        var filteredList = filteredFiles.ToList();

        if (filteredList.Count == 0 && !string.IsNullOrWhiteSpace(searchFilter.Value))
        {
            return new HeaderLayout(BuildHeader(), new NoResultsView());
        }

        if (filteredList.Count == 0)
        {
            return new HeaderLayout(BuildHeader(), null!);
        }

        var content = new List(filteredList.Select(f =>
        {
            var item = f;
            return new ListItem(item.FileName.Replace(".md", ""))
                .Content(Layout.Horizontal().Gap(1)
                         | new Badge(item.Project).Variant(BadgeVariant.Outline).Small()
                         | Text.Muted(item.Date.ToString("yyyy-MM-dd")).Small())
                .OnClick(() => selectedFile.Set(item.FilePath));
        }));

        return new HeaderLayout(BuildHeader(), content);
    }
}
