using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class ProjectCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-proj-cmd-test");
    private readonly string _originalTendrilHome;

    public ProjectCommandTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        var yaml = @"
projects: []
verifications: []
";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), yaml);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private ConfigService CreateConfig() => new();

    // --- Add Project ---

    [Fact]
    public void AddProject_CreatesNewProject()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig { Name = "MyProject", Color = "Blue", Context = "Test context" });
        config.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Projects);
        Assert.Equal("MyProject", reloaded.Settings.Projects[0].Name);
        Assert.Equal("Blue", reloaded.Settings.Projects[0].Color);
        Assert.Equal("Test context", reloaded.Settings.Projects[0].Context);
    }

    [Fact]
    public void AddProject_MultipleProjects()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig { Name = "Alpha" });
        config.Settings.Projects.Add(new ProjectConfig { Name = "Beta" });
        config.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal(2, reloaded.Settings.Projects.Count);
        Assert.Equal("Alpha", reloaded.Settings.Projects[0].Name);
        Assert.Equal("Beta", reloaded.Settings.Projects[1].Name);
    }

    // --- Remove Project ---

    [Fact]
    public void RemoveProject_RemovesExisting()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig { Name = "ToRemove" });
        config.Settings.Projects.Add(new ProjectConfig { Name = "ToKeep" });
        config.SaveSettings();

        var config2 = CreateConfig();
        var match = config2.Settings.Projects.First(p => p.Name.Equals("ToRemove", StringComparison.OrdinalIgnoreCase));
        config2.Settings.Projects.Remove(match);
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Projects);
        Assert.Equal("ToKeep", reloaded.Settings.Projects[0].Name);
    }

    // --- Set Project Fields ---

    [Fact]
    public void SetProject_UpdatesName()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig { Name = "Original" });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Projects[0].Name = "Renamed";
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal("Renamed", reloaded.Settings.Projects[0].Name);
    }

    [Fact]
    public void SetProject_UpdatesColor()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig { Name = "Test", Color = "Red" });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Projects[0].Color = "Green";
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal("Green", reloaded.Settings.Projects[0].Color);
    }

    [Fact]
    public void SetProject_UpdatesContext()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig { Name = "Test", Context = "Old" });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Projects[0].Context = "New context";
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal("New context", reloaded.Settings.Projects[0].Context);
    }

    // --- Add/Remove Repo ---

    [Fact]
    public void AddRepo_AddsToProject()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig { Name = "Test" });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Projects[0].Repos.Add(new RepoRef
        {
            Path = @"D:\Repos\MyRepo",
            PrRule = "default",
            SyncStrategy = "fetch"
        });
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Projects[0].Repos);
        Assert.Equal(@"D:\Repos\MyRepo", reloaded.Settings.Projects[0].Repos[0].Path);
    }

    [Fact]
    public void RemoveRepo_RemovesFromProject()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig
        {
            Name = "Test",
            Repos = [
                new RepoRef { Path = @"D:\Repos\Keep" },
                new RepoRef { Path = @"D:\Repos\Remove" }
            ]
        });
        config.SaveSettings();

        var config2 = CreateConfig();
        var match = config2.Settings.Projects[0].GetRepoRef(@"D:\Repos\Remove");
        Assert.NotNull(match);
        config2.Settings.Projects[0].Repos.Remove(match);
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Projects[0].Repos);
        Assert.Equal(@"D:\Repos\Keep", reloaded.Settings.Projects[0].Repos[0].Path);
    }

    // --- Add/Remove Verification ---

    [Fact]
    public void AddVerification_AddsToProject()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig { Name = "Test" });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Projects[0].Verifications.Add(new ProjectVerificationRef
        {
            Name = "UnitTests",
            Required = true
        });
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Projects[0].Verifications);
        Assert.Equal("UnitTests", reloaded.Settings.Projects[0].Verifications[0].Name);
        Assert.True(reloaded.Settings.Projects[0].Verifications[0].Required);
    }

    [Fact]
    public void RemoveVerification_RemovesFromProject()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig
        {
            Name = "Test",
            Verifications = [
                new ProjectVerificationRef { Name = "Keep" },
                new ProjectVerificationRef { Name = "Remove" }
            ]
        });
        config.SaveSettings();

        var config2 = CreateConfig();
        var match = config2.Settings.Projects[0].Verifications
            .First(v => v.Name.Equals("Remove", StringComparison.OrdinalIgnoreCase));
        config2.Settings.Projects[0].Verifications.Remove(match);
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Projects[0].Verifications);
        Assert.Equal("Keep", reloaded.Settings.Projects[0].Verifications[0].Name);
    }

    // --- Add/Remove Build Dependency ---

    [Fact]
    public void AddBuildDep_AddsToProject()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig { Name = "Test" });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Projects[0].BuildDependencies.Add("Framework");
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Projects[0].BuildDependencies);
        Assert.Equal("Framework", reloaded.Settings.Projects[0].BuildDependencies[0]);
    }

    [Fact]
    public void RemoveBuildDep_RemovesFromProject()
    {
        var config = CreateConfig();
        config.Settings.Projects.Add(new ProjectConfig
        {
            Name = "Test",
            BuildDependencies = ["Keep", "Remove"]
        });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Projects[0].BuildDependencies.RemoveAll(
            d => d.Equals("Remove", StringComparison.OrdinalIgnoreCase));
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Projects[0].BuildDependencies);
        Assert.Equal("Keep", reloaded.Settings.Projects[0].BuildDependencies[0]);
    }
}
