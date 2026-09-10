using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Settings;
using Ivy.Tendril.Apps.Settings.Blades;
using Ivy.Tendril.Apps.Settings.Dialogs;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Settings;

public class ProjectPortsAndEnvFilesGuidanceTests
{
    [Fact]
    public void ProjectPortsTableView_WhenEmpty_RendersHelpfulEmptyStateGuidance()
    {
        var ports = new State<Dictionary<string, ProjectPortConfig>>(new Dictionary<string, ProjectPortConfig>());
        var view = new ProjectPortsTableView(ports, _ => { });
        var result = view.Build();

        Assert.NotNull(result);
        var textBuilder = Assert.IsType<TextBuilder>(result);
        var textBlock = Assert.IsType<TextBlock>(textBuilder.Build());
        Assert.Contains("No service ports configured", textBlock.Content);
        Assert.Contains("dynamic port allocation across concurrent plan reviews", textBlock.Content);
    }

    [Fact]
    public void ProjectEnvFilesTableView_WhenEmpty_RendersHelpfulEmptyStateGuidance()
    {
        var envFiles = new State<List<ProjectEnvFileConfig>>(new List<ProjectEnvFileConfig>());
        var view = new ProjectEnvFilesTableView(envFiles, _ => { });
        var result = view.Build();

        Assert.NotNull(result);
        var textBuilder = Assert.IsType<TextBuilder>(result);
        var textBlock = Assert.IsType<TextBlock>(textBuilder.Build());
        Assert.Contains("No environment files configured", textBlock.Content);
        Assert.Contains("materialize .env templates and inject variables into plan worktrees", textBlock.Content);
    }

    [Fact]
    public void EditProjectPortDialog_BuildForm_ConfiguresHelpTooltipsOnAllFields()
    {
        var editName = new State<string>("");
        var editPort = new State<int>(3000);
        var editDescription = new State<string>("");

        var form = EditProjectPortDialog.BuildForm(editName, editPort, editDescription);

        Assert.NotNull(form);
        var layout = Assert.IsAssignableFrom<Ivy.Core.AbstractWidget>(form.Build());
        var fields = layout.Children.OfType<Field>().ToList();
        Assert.Equal(3, fields.Count);

        var nameField = fields[0];
        Assert.Equal("Name", nameField.Label);
        Assert.Equal("Port identifier referenced in environment overrides via ${ports.<name>} placeholders.", nameField.Help);

        var defaultPortField = fields[1];
        Assert.Equal("Default Port", defaultPortField.Label);
        Assert.Equal("Preferred port to allocate. If already in use by another plan or process, Tendril dynamically falls back to an available free port.", defaultPortField.Help);

        var descriptionField = fields[2];
        Assert.Equal("Description", descriptionField.Label);
        Assert.Equal("Optional summary of the service listening on this port.", descriptionField.Help);
    }

    [Fact]
    public void EditProjectEnvFileDialog_BuildForm_ConfiguresHelpTooltipsOnAllFields()
    {
        var editPath = new State<string>("");
        var editTemplate = new State<string>("");
        var editOverrides = new State<string>("");

        var form = EditProjectEnvFileDialog.BuildForm(editPath, editTemplate, editOverrides);

        Assert.NotNull(form);
        var layout = Assert.IsAssignableFrom<Ivy.Core.AbstractWidget>(form.Build());
        var fields = layout.Children.OfType<Field>().ToList();
        Assert.Equal(3, fields.Count);

        var pathField = fields[0];
        Assert.Equal("Path", pathField.Label);
        Assert.Equal("Relative path to the environment file recreated inside the plan worktree (e.g. apps/web/.env).", pathField.Help);

        var templateField = fields[1];
        Assert.Equal("Template", templateField.Label);
        Assert.Equal("Optional base template file (e.g. .env.example) copied into the worktree before applying overrides.", templateField.Help);

        var overridesField = fields[2];
        Assert.Equal("Overrides", overridesField.Label);
        Assert.Equal("KEY=VALUE override lines supporting ${ports.<name>}, ${env.<VAR>}, and %VAR% placeholder substitutions.", overridesField.Help);
    }

    [Fact]
    public void ProjectDetailView_Headers_IncludeInfoTooltipsWithGuidance()
    {
        var portsHeader = ProjectDetailView.BuildPortsHeader();
        var portsLayout = Assert.IsType<LayoutView>(portsHeader);
        var portsWidget = Assert.IsAssignableFrom<Ivy.Core.AbstractWidget>(portsLayout.Build());
        var portsTooltip = Assert.Single(portsWidget.Children.OfType<Tooltip>());
        var portsContentSlot = Assert.Single(portsTooltip.Children.OfType<Slot>().Where(s => s.Name == "Content"));
        var portsGuidance = Assert.IsType<string>(Assert.Single(portsContentSlot.Children));
        Assert.Equal("Named service ports dynamically allocated in plan worktrees to enable concurrent reviews without port conflicts.", portsGuidance);

        var envFilesHeader = ProjectDetailView.BuildEnvFilesHeader();
        var envFilesLayout = Assert.IsType<LayoutView>(envFilesHeader);
        var envFilesWidget = Assert.IsAssignableFrom<Ivy.Core.AbstractWidget>(envFilesLayout.Build());
        var envFilesTooltip = Assert.Single(envFilesWidget.Children.OfType<Tooltip>());
        var envFilesContentSlot = Assert.Single(envFilesTooltip.Children.OfType<Slot>().Where(s => s.Name == "Content"));
        var envFilesGuidance = Assert.IsType<string>(Assert.Single(envFilesContentSlot.Children));
        Assert.Equal("Environment files recreated in plan worktrees from templates and variable overrides.", envFilesGuidance);
    }
}
