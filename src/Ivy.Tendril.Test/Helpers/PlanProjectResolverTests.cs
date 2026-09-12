using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Helpers;

public class PlanProjectResolverTests
{
    private static ProjectConfig ConfiguredProject(string name, params string[] repos) =>
        new()
        {
            Name = name,
            Repos = repos.Select(r => new RepoRef { Path = r }).ToList()
        };

    [Fact]
    public void Resolves_Configured_Project_By_Name()
    {
        var configured = ConfiguredProject("MyProject", @"/repos/my-project");
        var available = new List<ProjectConfig> { configured };

        var resolved = PlanProjectResolver.ResolveProject("MyProject", available);

        Assert.Equal("MyProject", resolved.Name);
        Assert.Single(resolved.Repos);
        Assert.Equal(@"/repos/my-project", resolved.Repos[0].Path);
        Assert.False(resolved.IsAdHoc);
    }

    [Fact]
    public void Resolves_AdHoc_Project_From_Existing_Directory_Path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var available = new List<ProjectConfig>
            {
                ConfiguredProject("OtherProject", @"/repos/other")
            };

            var resolved = PlanProjectResolver.ResolveProject(tempDir, available);

            Assert.Equal(Path.GetFileName(tempDir), resolved.Name);
            Assert.Single(resolved.Repos);
            Assert.Equal(Path.GetFullPath(tempDir), resolved.Repos[0].Path);
            Assert.True(resolved.IsAdHoc);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Throws_When_Project_Not_Found_And_Path_Does_Not_Exist()
    {
        var available = new List<ProjectConfig>
        {
            ConfiguredProject("Alpha", @"/repos/alpha"),
            ConfiguredProject("Beta", @"/repos/beta")
        };

        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid():N}");

        var ex = Assert.Throws<ArgumentException>(() =>
            PlanProjectResolver.ResolveProject(nonExistentPath, available));

        Assert.Contains($"Project '{nonExistentPath}' not found", ex.Message);
        Assert.Contains("Alpha", ex.Message);
        Assert.Contains("Beta", ex.Message);
    }
}
