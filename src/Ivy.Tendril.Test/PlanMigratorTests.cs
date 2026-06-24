using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class PlanMigratorTests
{
    [Fact]
    public void LatestVersion_MatchesPlanYamlCurrentSchemaVersion()
    {
        // PlanYaml.CurrentSchemaVersion is a compile-time const used to stamp newly-written plans; it
        // must track the highest migration version or new plans would be re-migrated on every startup.
        Assert.Equal(PlanYaml.CurrentSchemaVersion, new PlanMigrator().LatestVersion);
    }

    [Fact]
    public void RealMigrations_AreSequentiallyNumberedFromOne()
    {
        // Construction validates the discovered set; this throwing would mean a gap/duplicate in the
        // PlanMigration_NNN files.
        var ex = Record.Exception(() => new PlanMigrator());
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_Throws_OnDuplicateVersions()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PlanMigrator([new FakeMigration(1), new FakeMigration(1)]));
    }

    [Fact]
    public void Constructor_Throws_OnGapInSequence()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PlanMigrator([new FakeMigration(1), new FakeMigration(3)]));
    }

    [Fact]
    public void RenameLegacyStateNames_MapsBuildingToCreating()
    {
        var migration = new PlanMigration_001_RenameLegacyStateNames();
        var result = migration.Apply(Context("state: Building\nproject: Test\n", "Building"));

        Assert.Contains("state: Creating", result);
        Assert.DoesNotContain("Building", result);
    }

    [Fact]
    public void RenameLegacyStateNames_LeavesUnknownStatesUntouched()
    {
        var migration = new PlanMigration_001_RenameLegacyStateNames();
        const string yaml = "state: Draft\nproject: Test\n";

        Assert.Equal(yaml, migration.Apply(Context(yaml, "Draft")));
    }

    [Fact]
    public void NormalizeYamlStructure_FlattensObjectStyleRepos()
    {
        var migration = new PlanMigration_003_NormalizeYamlStructure();
        const string yaml = "state: Draft\nrepos:\n  - name: r\n    path: C:\\repos\\r\n    branch: main\n";

        var result = migration.Apply(Context(yaml, "Draft"));

        // Object-style repo is flattened to a plain (quoted) path string; the structured sub-keys are dropped.
        Assert.Contains("C:\\repos\\r", result);
        Assert.DoesNotContain("name: r", result);
        Assert.DoesNotContain("branch:", result);
    }

    private static PlanMigrationContext Context(string yaml, string state) => new()
    {
        PlanFolder = "unused",
        FolderName = "00001-Test",
        Yaml = yaml,
        State = state,
        Logger = NullLogger.Instance
    };

    private sealed class FakeMigration(int version) : IPlanMigration
    {
        public int Version => version;
        public string Description => $"fake {version}";
        public string Apply(PlanMigrationContext context) => context.Yaml;
    }
}
