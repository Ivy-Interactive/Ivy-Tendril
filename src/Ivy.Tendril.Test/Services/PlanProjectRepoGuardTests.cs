using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Test.Services;

public class PlanProjectRepoGuardTests
{
    private static ProjectConfig Project(params string[] repoPaths) =>
        new() { Name = "Proj", Repos = repoPaths.Select(p => new RepoRef { Path = p }).ToList() };

    [Fact]
    public void Allows_Repo_In_Project()
    {
        var project = Project(@"C:\repos\Ivy-Tendril");
        // No throw.
        PlanProjectRepoGuard.EnsureReposBelongToProject([@"C:\repos\Ivy-Tendril"], project);
    }

    [Fact]
    public void Allows_Build_Dependency()
    {
        var project = Project(@"C:\repos\Ivy-Tendril");
        project.BuildDependencies = [@"C:\deps\Ivy-Framework"];
        PlanProjectRepoGuard.EnsureReposBelongToProject([@"C:\deps\Ivy-Framework"], project);
    }

    [Fact]
    public void Throws_For_Repo_Outside_Project()
    {
        var project = Project(@"C:\repos\Ivy-Tendril");

        var ex = Assert.Throws<ArgumentException>(() =>
            PlanProjectRepoGuard.EnsureReposBelongToProject([@"C:\repos\Ivy-Framework"], project));

        Assert.Contains("Ivy-Framework", ex.Message);
        Assert.Contains("Proj", ex.Message);
    }

    [Fact]
    public void Matches_By_Folder_Name_Regardless_Of_Parent_Path()
    {
        // A plan may store a different absolute path with the same repo folder name; treated as in-project,
        // matching how JobLauncher resolves project membership.
        var project = Project(@"C:\repos\Ivy-Tendril");
        PlanProjectRepoGuard.EnsureReposBelongToProject([@"D:\elsewhere\Ivy-Tendril"], project);
    }

    [Fact]
    public void Reports_All_Offending_Repos()
    {
        var project = Project(@"C:\repos\Ivy-Tendril");

        var ex = Assert.Throws<ArgumentException>(() =>
            PlanProjectRepoGuard.EnsureReposBelongToProject(
                [@"C:\repos\Ivy-Tendril", @"C:\repos\Ivy-Framework", @"C:\repos\Other"], project));

        // Offending list (before "Allowed repos:") names the outside repos but not the in-project one.
        var offendingSegment = ex.Message[..ex.Message.IndexOf("Allowed repos", StringComparison.Ordinal)];
        Assert.Contains("Ivy-Framework", offendingSegment);
        Assert.Contains("Other", offendingSegment);
        Assert.DoesNotContain("Ivy-Tendril", offendingSegment);
    }

    [Fact]
    public void Allows_Repo_When_Project_Is_AdHoc()
    {
        var project = new ProjectConfig
        {
            Name = "AdHocProj",
            Repos = [new RepoRef { Path = @"/some/path" }],
            Meta = new Dictionary<string, object> { ["adhoc"] = true }
        };

        // No throw even for unrelated repo path.
        PlanProjectRepoGuard.EnsureReposBelongToProject([@"/other/unrelated/repo"], project);
    }

    [Fact]
    public void Allows_Unmanaged_Workspace_Path()
    {
        var project = new ProjectConfig
        {
            Name = "UnmanagedWorkspace",
            Repos = [new RepoRef { Path = @"/unmanaged/workspace" }],
            Meta = new Dictionary<string, object> { ["adhoc"] = true }
        };

        // No throw for unmanaged workspace path.
        PlanProjectRepoGuard.EnsureReposBelongToProject([@"/unmanaged/workspace/subfolder"], project);
    }
}
