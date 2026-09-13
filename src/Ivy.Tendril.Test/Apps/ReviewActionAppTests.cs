using Ivy.Tendril.Apps.ReviewAction;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.TestHelpers;

namespace Ivy.Tendril.Test.Apps;

public class ReviewActionAppTests
{
    private static StubConfigService BuildConfig() => new([
        new ProjectConfig
        {
            Name = "Tendril",
            ReviewActions =
            [
                new ReviewActionConfig { Name = "Run App", Condition = "$true", Command = "dotnet run" },
                new ReviewActionConfig { Name = "No Command", Condition = "$true", Command = "" }
            ]
        }
    ]);

    [Fact]
    public void ResolveAction_KnownActionWithCommand_ReturnsAction()
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), "Tendril", "Run App");

        Assert.NotNull(action);
        Assert.Equal("dotnet run", action.Command);
    }

    [Fact]
    public void ResolveAction_UnknownActionName_ReturnsNull()
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), "Tendril", "Does Not Exist");

        Assert.Null(action);
    }

    [Fact]
    public void ResolveAction_ActionWithEmptyCommand_ReturnsNull()
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), "Tendril", "No Command");

        Assert.Null(action);
    }

    [Fact]
    public void ResolveAction_UnknownProject_ReturnsNull()
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), "NoSuchProject", "Run App");

        Assert.Null(action);
    }

    [Theory]
    [InlineData(null, "Run App")]
    [InlineData("Tendril", null)]
    public void ResolveAction_MissingProjectOrActionName_ReturnsNull(string? project, string? actionName)
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), project, actionName);

        Assert.Null(action);
    }

    [Fact]
    public void ResolveWorkingDirectory_PlanNotNull_ReturnsPlanFolderPath()
    {
        var metadata = new PlanMetadata(
            1, "Tendril", "Feature", "Test", PlanStatus.Draft,
            [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        var plan = new PlanFile(metadata, "", "/plans/00001-Test", "");

        var workDir = ReviewActionApp.ResolveWorkingDirectory(BuildConfig(), plan, null);

        Assert.Equal("/plans/00001-Test", workDir);
    }

    [Fact]
    public void ResolveWorkingDirectory_ProjectWithNoRepos_ReturnsTendrilHome()
    {
        var config = BuildConfig();
        var project = new ProjectConfig
        {
            Name = "Tendril",
            Repos = []
        };

        var workDir = ReviewActionApp.ResolveWorkingDirectory(config, null, project);

        Assert.Equal(config.TendrilHome, workDir);
    }

    [Fact]
    public void ResolveWorkingDirectory_NullPlanAndProject_ReturnsNull()
    {
        var workDir = ReviewActionApp.ResolveWorkingDirectory(BuildConfig(), null, null);

        Assert.Null(workDir);
    }

    // --- Ports & environment ---

    private static PlanFile BuildPlan(Dictionary<string, int>? allocatedPorts = null, string folderPath = "/plans/00001-Test")
    {
        var metadata = new PlanMetadata(
            1, "Tendril", "Feature", "Test", PlanStatus.Draft,
            [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null,
            AllocatedPorts: allocatedPorts);
        return new PlanFile(metadata, "", folderPath, "");
    }

    private static ProjectConfig BuildProjectWithPorts(params (string Name, int DefaultPort)[] ports)
    {
        var project = new ProjectConfig { Name = "Tendril" };
        foreach (var (name, defaultPort) in ports)
            project.Ports[name] = new ProjectPortConfig { DefaultPort = defaultPort };
        return project;
    }

    [Fact]
    public void ResolvePorts_AllocatedPortWins_OverConfiguredDefault()
    {
        var ports = ReviewActionApp.ResolvePorts(
            BuildPlan(new Dictionary<string, int> { ["backend"] = 31234 }),
            BuildProjectWithPorts(("backend", 3001)));

        Assert.Equal(31234, ports["backend"]);
    }

    [Fact]
    public void ResolvePorts_NoPlan_FallsBackToConfiguredDefaults()
    {
        // Review actions also run outside a plan, where nothing has been allocated.
        var ports = ReviewActionApp.ResolvePorts(null, BuildProjectWithPorts(("backend", 3001)));

        Assert.Equal(3001, ports["backend"]);
    }

    [Fact]
    public void ResolvePorts_FollowsTheProjectsConfiguredOrder()
    {
        var ports = ReviewActionApp.ResolvePorts(
            BuildPlan(new Dictionary<string, int> { ["frontend"] = 3000, ["backend"] = 3001 }),
            BuildProjectWithPorts(("backend", 3001), ("frontend", 3000)));

        // The first configured port is the primary one %PORT% resolves to, so the order must not
        // depend on the allocation dictionary's insertion order.
        Assert.Equal(new[] { "backend", "frontend" }, ports.Keys);
    }

    [Fact]
    public void ResolvePorts_AllocatedNameMissingFromConfig_IsStillIncluded()
    {
        var ports = ReviewActionApp.ResolvePorts(
            BuildPlan(new Dictionary<string, int> { ["retired"] = 31234 }),
            BuildProjectWithPorts(("backend", 3001)));

        Assert.Equal(31234, ports["retired"]);
    }

    [Fact]
    public void ResolvePorts_ZeroDefaultAndNoAllocation_IsOmitted()
    {
        var ports = ReviewActionApp.ResolvePorts(null, BuildProjectWithPorts(("backend", 0)));

        Assert.Empty(ports);
    }

    [Fact]
    public void ResolvePorts_NoPlanAndNoProject_ReturnsEmpty()
    {
        Assert.Empty(ReviewActionApp.ResolvePorts(null, null));
    }

    [Fact]
    public void InterpolateCommand_ExpandsNamedPortPlaceholder()
    {
        var command = ReviewActionApp.InterpolateCommand(
            "dotnet run --urls http://localhost:${ports.backend}",
            new Dictionary<string, int> { ["backend"] = 31234 });

        Assert.Equal("dotnet run --urls http://localhost:31234", command);
    }

    [Fact]
    public void InterpolateCommand_ExpandsBarePortToken_ToTheFirstPort()
    {
        var ports = new Dictionary<string, int> { ["backend"] = 31234, ["frontend"] = 31235 };

        var command = ReviewActionApp.InterpolateCommand("npm run dev -- --port %PORT%", ports);

        Assert.Equal("npm run dev -- --port 31234", command);
    }

    [Fact]
    public void InterpolateCommand_NoPortsAllocated_LeavesCommandUnchanged()
    {
        var command = ReviewActionApp.InterpolateCommand("npm run dev -- --port %PORT%", new Dictionary<string, int>());

        Assert.Equal("npm run dev -- --port %PORT%", command);
    }

    [Fact]
    public void BuildEnvironment_ExposesPerPortAndPrimaryVariables()
    {
        var env = ReviewActionApp.BuildEnvironment(
            BuildPlan(new Dictionary<string, int> { ["backend"] = 31234, ["web-ui"] = 31235 }),
            BuildProjectWithPorts(("backend", 3001), ("web-ui", 3000)));

        Assert.Equal("31234", env["PORT_BACKEND"]);
        Assert.Equal("31235", env["PORT_WEB_UI"]);
        Assert.Equal("31234", env["PORT"]);
    }

    [Fact]
    public void BuildEnvironment_ExposesPlanContext()
    {
        var env = ReviewActionApp.BuildEnvironment(BuildPlan(), BuildProjectWithPorts());

        Assert.Equal("00001", env["PLAN_ID"]);
        Assert.Equal("/plans/00001-Test", env["PLAN_FOLDER"]);
        Assert.Equal("Tendril", env["PROJECT_NAME"]);
    }

    [Fact]
    public void BuildEnvironment_NoWorktreeOnDisk_OmitsWorktreeDir()
    {
        var env = ReviewActionApp.BuildEnvironment(BuildPlan(), BuildProjectWithPorts());

        Assert.False(env.ContainsKey("WORKTREE_DIR"));
    }

    [Fact]
    public void BuildEnvironment_NoPlan_StillNamesTheProject()
    {
        var env = ReviewActionApp.BuildEnvironment(null, BuildProjectWithPorts(("backend", 3001)));

        Assert.Equal("Tendril", env["PROJECT_NAME"]);
        Assert.Equal("3001", env["PORT_BACKEND"]);
        Assert.False(env.ContainsKey("PLAN_ID"));
    }

    [Fact]
    public void BuildEnvironment_NoPlanAndNoProject_ReturnsEmpty()
    {
        Assert.Empty(ReviewActionApp.BuildEnvironment(null, null));
    }
}
