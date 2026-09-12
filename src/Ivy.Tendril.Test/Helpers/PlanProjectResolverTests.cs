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
    public void Resolves_AdHoc_Project_To_Git_Root_When_Subfolder_Provided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"tendril-test-adhoc-root-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(rootDir, ".git");
        var subDir = Path.Combine(rootDir, "src", "MyModule");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(subDir);

        try
        {
            var available = new List<ProjectConfig>
            {
                ConfiguredProject("OtherProject", @"/repos/other")
            };

            var resolved = PlanProjectResolver.ResolveProject(subDir, available);

            Assert.Equal(Path.GetFileName(rootDir), resolved.Name);
            Assert.Single(resolved.Repos);
            Assert.Equal(Path.GetFullPath(rootDir), resolved.Repos[0].Path);
            Assert.True(resolved.IsAdHoc);
            Assert.True(resolved.Meta.TryGetValue("targetPath", out var targetPath));
            Assert.Equal(Path.GetFullPath(subDir), targetPath?.ToString());
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, true);
        }
    }

    [Fact]
    public void Resolves_Configured_Project_When_Subfolder_Belongs_To_Configured_Repo()
    {
        var repoDir = Path.Combine(Path.GetTempPath(), $"tendril-test-conf-repo-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(repoDir, ".git");
        var subDir = Path.Combine(repoDir, "src", "NestedPackage");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(subDir);

        try
        {
            var configured = ConfiguredProject("ExistingProject", repoDir);
            var available = new List<ProjectConfig> { configured };

            var resolved = PlanProjectResolver.ResolveProject(subDir, available);

            Assert.Same(configured, resolved);
            Assert.Equal("ExistingProject", resolved.Name);
            Assert.False(resolved.IsAdHoc);
        }
        finally
        {
            if (Directory.Exists(repoDir))
                Directory.Delete(repoDir, true);
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
