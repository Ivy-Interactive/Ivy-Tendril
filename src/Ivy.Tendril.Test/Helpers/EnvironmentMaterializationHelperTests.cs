using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Helpers;

public class EnvironmentMaterializationHelperTests : IDisposable
{
    private readonly TempDirectoryFixture _worktree = new("tendril-env-mat-test");

    public void Dispose() => _worktree.Dispose();

    private static ProjectConfig BuildProject(ProjectEnvFileConfig envFile, params (string Name, int DefaultPort)[] ports)
    {
        var project = new ProjectConfig { Name = "Env" };
        project.EnvFiles.Add(envFile);
        foreach (var (name, defaultPort) in ports)
            project.Ports[name] = new ProjectPortConfig { DefaultPort = defaultPort };
        return project;
    }

    private void WriteTemplate(string relativePath, string content) =>
        File.WriteAllText(Path.Combine(_worktree.Path, relativePath), content);

    private string ReadWritten(string relativePath) =>
        File.ReadAllText(Path.Combine(_worktree.Path, relativePath));

    [Fact]
    public void MaterializeEnvFiles_TemplateAndOverrides_MergesBoth()
    {
        WriteTemplate(".env.example", "DATABASE_URL=postgres://localhost/dev\nLOG_LEVEL=info\n");
        var project = BuildProject(new ProjectEnvFileConfig
        {
            Path = ".env",
            Template = ".env.example",
            Overrides = new Dictionary<string, string> { ["LOG_LEVEL"] = "debug", ["API_KEY"] = "local" }
        });

        var written = EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int>(), _worktree.Path);

        Assert.Equal(1, written);
        Assert.Equal(
            "DATABASE_URL=postgres://localhost/dev\nLOG_LEVEL=debug\nAPI_KEY=local\n",
            ReadWritten(".env"));
    }

    [Fact]
    public void MaterializeEnvFiles_PortPlaceholder_ResolvesToAllocatedPort()
    {
        var project = BuildProject(
            new ProjectEnvFileConfig
            {
                Path = ".env",
                Overrides = new Dictionary<string, string>
                {
                    ["PORT"] = "${ports.backend}",
                    ["VITE_API_URL"] = "http://localhost:${ports.backend}/api"
                }
            },
            ("backend", 3001));

        EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int> { ["backend"] = 31234 }, _worktree.Path);

        Assert.Equal("PORT=31234\nVITE_API_URL=http://localhost:31234/api\n", ReadWritten(".env"));
    }

    [Fact]
    public void MaterializeEnvFiles_PortNotAllocated_FallsBackToConfiguredDefault()
    {
        var project = BuildProject(
            new ProjectEnvFileConfig
            {
                Path = ".env",
                Overrides = new Dictionary<string, string> { ["PORT"] = "${ports.backend}" }
            },
            ("backend", 3001));

        EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int>(), _worktree.Path);

        Assert.Equal("PORT=3001\n", ReadWritten(".env"));
    }

    [Fact]
    public void MaterializeEnvFiles_UnknownPortName_LeavesPlaceholderVisible()
    {
        var project = BuildProject(new ProjectEnvFileConfig
        {
            Path = ".env",
            Overrides = new Dictionary<string, string> { ["PORT"] = "${ports.nope}" }
        });

        EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int>(), _worktree.Path);

        // An empty value would look like a working config; the literal placeholder does not.
        Assert.Equal("PORT=${ports.nope}\n", ReadWritten(".env"));
    }

    [Fact]
    public void MaterializeEnvFiles_TemplatePortPlaceholder_IsExpandedToo()
    {
        WriteTemplate(".env.example", "PORT=${ports.backend}\n");
        var project = BuildProject(
            new ProjectEnvFileConfig { Path = ".env", Template = ".env.example" },
            ("backend", 3001));

        EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int> { ["backend"] = 31235 }, _worktree.Path);

        Assert.Equal("PORT=31235\n", ReadWritten(".env"));
    }

    [Fact]
    public void MaterializeEnvFiles_EnvPlaceholder_ReadsHostEnvironment()
    {
        var variable = $"TENDRIL_ENV_MAT_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, "from-host");
        try
        {
            var project = BuildProject(new ProjectEnvFileConfig
            {
                Path = ".env",
                Overrides = new Dictionary<string, string>
                {
                    ["PRESENT"] = $"${{env.{variable}}}",
                    ["ABSENT"] = "${env.TENDRIL_ENV_MAT_TEST_UNSET}"
                }
            });

            EnvironmentMaterializationHelper.MaterializeEnvFiles(
                project, new Dictionary<string, int>(), _worktree.Path);

            Assert.Equal("PRESENT=from-host\nABSENT=\n", ReadWritten(".env"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void MaterializeEnvFiles_PreservesCommentsAndBlankLines()
    {
        WriteTemplate(".env.example", "# Database\n\nDATABASE_URL=postgres://localhost/dev\n");
        var project = BuildProject(new ProjectEnvFileConfig { Path = ".env", Template = ".env.example" });

        EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int>(), _worktree.Path);

        Assert.Equal("# Database\n\nDATABASE_URL=postgres://localhost/dev\n", ReadWritten(".env"));
    }

    [Fact]
    public void MaterializeEnvFiles_UsesUnixLineEndings()
    {
        var project = BuildProject(new ProjectEnvFileConfig
        {
            Path = ".env",
            Overrides = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" }
        });

        EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int>(), _worktree.Path);

        // A trailing CR would become part of the value in Node and Python dotenv parsers.
        Assert.DoesNotContain('\r', ReadWritten(".env"));
    }

    [Fact]
    public void MaterializeEnvFiles_NestedPath_CreatesDirectories()
    {
        var project = BuildProject(new ProjectEnvFileConfig
        {
            Path = Path.Combine("apps", "web", ".env"),
            Overrides = new Dictionary<string, string> { ["A"] = "1" }
        });

        var written = EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int>(), _worktree.Path);

        Assert.Equal(1, written);
        Assert.True(File.Exists(Path.Combine(_worktree.Path, "apps", "web", ".env")));
    }

    [Fact]
    public void MaterializeEnvFiles_MissingTemplate_WritesOverridesOnly()
    {
        var project = BuildProject(new ProjectEnvFileConfig
        {
            Path = ".env",
            Template = "does-not-exist.env",
            Overrides = new Dictionary<string, string> { ["A"] = "1" }
        });

        var written = EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int>(), _worktree.Path);

        Assert.Equal(1, written);
        Assert.Equal("A=1\n", ReadWritten(".env"));
    }

    [Fact]
    public void MaterializeEnvFiles_BlankPath_IsSkipped()
    {
        var project = BuildProject(new ProjectEnvFileConfig { Path = "  " });

        var written = EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int>(), _worktree.Path);

        Assert.Equal(0, written);
    }

    [Fact]
    public void MaterializeEnvFiles_ExportPrefixedTemplateKey_IsOverridden()
    {
        WriteTemplate(".env.example", "export LOG_LEVEL=info\n");
        var project = BuildProject(new ProjectEnvFileConfig
        {
            Path = ".env",
            Template = ".env.example",
            Overrides = new Dictionary<string, string> { ["LOG_LEVEL"] = "debug" }
        });

        EnvironmentMaterializationHelper.MaterializeEnvFiles(
            project, new Dictionary<string, int>(), _worktree.Path);

        Assert.Equal("LOG_LEVEL=debug\n", ReadWritten(".env"));
    }

    [Fact]
    public void ResolveEnvValues_ReturnsResolvedPairsWithoutWriting()
    {
        var envFile = new ProjectEnvFileConfig
        {
            Path = ".env",
            Overrides = new Dictionary<string, string> { ["PORT"] = "${ports.backend}" }
        };
        var project = BuildProject(envFile, ("backend", 3001));

        var values = EnvironmentMaterializationHelper.ResolveEnvValues(
            envFile, project, new Dictionary<string, int> { ["backend"] = 31236 }, _worktree.Path);

        Assert.Equal(new KeyValuePair<string, string>("PORT", "31236"), Assert.Single(values));
        Assert.False(File.Exists(Path.Combine(_worktree.Path, ".env")));
    }

    [Fact]
    public void ExpandPortPlaceholders_NoPlaceholder_ReturnsInputUnchanged()
    {
        var result = EnvironmentMaterializationHelper.ExpandPortPlaceholders(
            "dotnet run", new Dictionary<string, int> { ["backend"] = 3001 });

        Assert.Equal("dotnet run", result);
    }
}
