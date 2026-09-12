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

    [Fact]
    public void Resolves_Correct_Monorepo_Project_When_Multiple_Projects_Share_Git_Root_By_Repo_Subfolder()
    {
        var repoDir = Path.Combine(Path.GetTempPath(), $"tendril-test-monorepo-repo-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(repoDir, ".git");
        var subDirA = Path.Combine(repoDir, "apps", "frontend");
        var subDirB = Path.Combine(repoDir, "apps", "backend");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(subDirA);
        Directory.CreateDirectory(subDirB);

        try
        {
            var projectA = ConfiguredProject("ProjectFrontend", subDirA);
            var projectB = ConfiguredProject("ProjectBackend", subDirB);
            var available = new List<ProjectConfig> { projectA, projectB };

            var resolved = PlanProjectResolver.ResolveProject(subDirB, available);

            Assert.Same(projectB, resolved);
            Assert.Equal("ProjectBackend", resolved.Name);
            Assert.False(resolved.IsAdHoc);
        }
        finally
        {
            if (Directory.Exists(repoDir))
                Directory.Delete(repoDir, true);
        }
    }

    [Fact]
    public void Resolves_Correct_Monorepo_Project_Using_ProjectConfig_Subdirectory()
    {
        var repoDir = Path.Combine(Path.GetTempPath(), $"tendril-test-monorepo-sub-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(repoDir, ".git");
        var subDirA = Path.Combine(repoDir, "apps", "frontend");
        var subDirASrc = Path.Combine(subDirA, "src");
        var subDirB = Path.Combine(repoDir, "apps", "backend");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(subDirASrc);
        Directory.CreateDirectory(subDirB);

        try
        {
            var projectA = new ProjectConfig
            {
                Name = "ProjectFrontend",
                Subdirectory = "apps/frontend",
                Repos = [new RepoRef { Path = repoDir }]
            };
            var projectB = new ProjectConfig
            {
                Name = "ProjectBackend",
                Subdirectory = "apps/backend",
                Repos = [new RepoRef { Path = repoDir }]
            };
            var available = new List<ProjectConfig> { projectA, projectB };

            var resolved = PlanProjectResolver.ResolveProject(subDirASrc, available);

            Assert.Same(projectA, resolved);
            Assert.Equal("ProjectFrontend", resolved.Name);
            Assert.False(resolved.IsAdHoc);
        }
        finally
        {
            if (Directory.Exists(repoDir))
                Directory.Delete(repoDir, true);
        }
    }

    [Fact]
    public void Resolves_Most_Specific_Subdirectory_When_Nested()
    {
        var repoDir = Path.Combine(Path.GetTempPath(), $"tendril-test-monorepo-nested-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(repoDir, ".git");
        var appsDir = Path.Combine(repoDir, "apps");
        var frontendDir = Path.Combine(appsDir, "frontend");
        var srcDir = Path.Combine(frontendDir, "src");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(srcDir);

        try
        {
            var projectApps = new ProjectConfig
            {
                Name = "ProjectApps",
                Subdirectory = "apps",
                Repos = [new RepoRef { Path = repoDir }]
            };
            var projectFrontend = new ProjectConfig
            {
                Name = "ProjectFrontend",
                Subdirectory = "apps/frontend",
                Repos = [new RepoRef { Path = repoDir }]
            };
            var available = new List<ProjectConfig> { projectApps, projectFrontend };

            var resolved = PlanProjectResolver.ResolveProject(srcDir, available);

            Assert.Same(projectFrontend, resolved);
            Assert.Equal("ProjectFrontend", resolved.Name);
            Assert.False(resolved.IsAdHoc);
        }
        finally
        {
            if (Directory.Exists(repoDir))
                Directory.Delete(repoDir, true);
        }
    }

    [Fact]
    public void Resolves_Root_Project_When_Subfolder_Does_Not_Match_Specific_Subprojects()
    {
        var repoDir = Path.Combine(Path.GetTempPath(), $"tendril-test-monorepo-root-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(repoDir, ".git");
        var frontendDir = Path.Combine(repoDir, "apps", "frontend");
        var toolsScriptsDir = Path.Combine(repoDir, "tools", "scripts");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(frontendDir);
        Directory.CreateDirectory(toolsScriptsDir);

        try
        {
            var projectRoot = new ProjectConfig
            {
                Name = "MonorepoRoot",
                Repos = [new RepoRef { Path = repoDir }]
            };
            var projectFrontend = new ProjectConfig
            {
                Name = "ProjectFrontend",
                Subdirectory = "apps/frontend",
                Repos = [new RepoRef { Path = repoDir }]
            };
            var available = new List<ProjectConfig> { projectFrontend, projectRoot };

            var resolved = PlanProjectResolver.ResolveProject(toolsScriptsDir, available);

            Assert.Same(projectRoot, resolved);
            Assert.Equal("MonorepoRoot", resolved.Name);
            Assert.False(resolved.IsAdHoc);
        }
        finally
        {
            if (Directory.Exists(repoDir))
                Directory.Delete(repoDir, true);
        }
    }

    [Fact]
    public void Does_Not_Match_Subdirectory_When_Prefix_Is_Partial_Directory_Name()
    {
        var repoDir = Path.Combine(Path.GetTempPath(), $"tendril-test-monorepo-prefix-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(repoDir, ".git");
        var frontendDir = Path.Combine(repoDir, "apps", "frontend");
        var frontendExtraDir = Path.Combine(repoDir, "apps", "frontend-extra");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(frontendDir);
        Directory.CreateDirectory(frontendExtraDir);

        try
        {
            var projectFrontend = new ProjectConfig
            {
                Name = "ProjectFrontend",
                Subdirectory = "apps/frontend",
                Repos = [new RepoRef { Path = repoDir }]
            };
            var available = new List<ProjectConfig> { projectFrontend };

            // Single candidate whose subdirectory conflicts with target path: falls through to ad-hoc
            var resolved = PlanProjectResolver.ResolveProject(frontendExtraDir, available);

            Assert.True(resolved.IsAdHoc);
            Assert.Equal(Path.GetFileName(repoDir), resolved.Name);
        }
        finally
        {
            if (Directory.Exists(repoDir))
                Directory.Delete(repoDir, true);
        }
    }

    [Fact]
    public void Resolves_Using_RepoRef_Subdirectory()
    {
        var repoDir = Path.Combine(Path.GetTempPath(), $"tendril-test-reporef-sub-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(repoDir, ".git");
        var adminDir = Path.Combine(repoDir, "apps", "admin");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(adminDir);

        try
        {
            var projectAdmin = new ProjectConfig
            {
                Name = "ProjectAdmin",
                Repos = [new RepoRef { Path = repoDir, Subdirectory = "apps/admin" }]
            };
            var available = new List<ProjectConfig> { projectAdmin };

            var resolved = PlanProjectResolver.ResolveProject(adminDir, available);

            Assert.Same(projectAdmin, resolved);
            Assert.Equal("ProjectAdmin", resolved.Name);
            Assert.False(resolved.IsAdHoc);
        }
        finally
        {
            if (Directory.Exists(repoDir))
                Directory.Delete(repoDir, true);
        }
    }
}
