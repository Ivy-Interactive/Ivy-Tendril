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

        var projectList = Layout.Vertical().Gap(2);
        for (var i = 0; i < projects.Count; i++)
        {
            var idx = i;
            var project = projects[idx];

            var repoInfo = Layout.Horizontal().Gap(2).AlignContent(Align.Center);
            foreach (var repo in project.Repos)
            {
                repoInfo |= Text.Block(repo.Path).Muted().Small();
                repoInfo |= new Badge(repo.PrRule).Variant(BadgeVariant.Outline).Small();
            }

            var row = Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                      | Text.Block(project.Name).Bold()
                      | repoInfo
                      | new Spacer().Width(Size.Grow())
                      | new Button().Icon(Icons.Pencil).Ghost().Small()
                          .OnClick(() => editIndex.Set(idx))
                          .WithTooltip("Edit project")
                      | new Button().Icon(Icons.Trash).Ghost().Small()
                          .OnClick(() => deleteIndex.Set(idx));

            projectList |= new Button().Inline().OnClick(() => editIndex.Set(idx))
                               .Content(row);
        }

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(200)))
               | Text.Block("Projects").Bold()
               | Text.Block("Manage projects, their repositories, and verification assignments.").Muted().Small()
               | projectList
               | new Button("Add Project").Icon(Icons.Plus).Outline().OnClick(() =>
               {
                   editIndex.Set(null);
               })
               | new EditProjectDialog(editIndex, projects, allVerifications, config, client, refreshToken)
               | new DeleteProjectDialog(deleteIndex, projects, config, client, refreshToken);
    }
}
