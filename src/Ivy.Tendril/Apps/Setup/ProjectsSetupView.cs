using Ivy.Tendril.Apps.Setup.Dialogs;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Setup;

public class ProjectsSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();
        var editIndex = UseState<int?>(-1);
        var deleteIndex = UseState<int?>(-1);

        var projects = config.Settings.Projects;
        var allVerifications = config.Settings.Verifications.Select(v => v.Name).ToList();

        var rows = projects.Select((p, i) => new ProjectRow(p.Name, p.Repos, i)).ToList();

        var table = new TableBuilder<ProjectRow>(rows)
            .Builder(t => t.Repos, f => f.Func<ProjectRow, List<RepoRef>>(repos =>
            {
                var layout = Layout.Horizontal().Gap(2).AlignContent(Align.Left);
                foreach (var repo in repos)
                {
                    layout |= Text.Block(repo.Path).Muted().Small();
                    layout |= new Badge(repo.PrRule).Variant(BadgeVariant.Outline).Small();
                }
                return layout;
            }))
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<ProjectRow, int>(idx =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit this project").OnClick(() =>
                {
                    editIndex.Set(idx);
                })
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete this project").OnClick(() =>
                {
                    deleteIndex.Set(idx);
                })
            ))
            .ColumnWidth(t => t.Repos, Size.Grow())
            .ColumnWidth(t => t.Index, Size.Px(88));

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(200)))
               | Text.Block("Projects").Bold()
               | Text.Block("Manage projects, their repositories, and verification assignments.").Muted().Small()
               | table
               | new Button("Add Project").Icon(Icons.Plus).Outline().OnClick(() =>
               {
                   editIndex.Set(null);
               })
               | new EditProjectDialog(editIndex, projects, allVerifications, config, client, refreshToken)
               | new DeleteProjectDialog(deleteIndex, projects, config, client, refreshToken);
    }

    private record ProjectRow(string Name, List<RepoRef> Repos, int Index);
}
