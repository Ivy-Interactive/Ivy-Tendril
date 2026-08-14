using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class ProjectsSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();

        var projects = config.Settings.Projects;

        if (projects.Count == 0)
        {
            return new AddProjectView(config, client, refreshToken);
        }

        return new ProjectDetailView(0, projects, config, client, refreshToken);
    }
}
