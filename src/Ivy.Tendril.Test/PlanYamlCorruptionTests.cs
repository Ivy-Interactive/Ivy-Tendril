using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Test.Helpers;

namespace Ivy.Tendril.Test;

public class PlanYamlCorruptionTests : IClassFixture<ConfigServiceFixture>
{
    private readonly ConfigServiceFixture _fixture;
    private readonly string _testPlansDir;

    public PlanYamlCorruptionTests(ConfigServiceFixture fixture)
    {
        _fixture = fixture;
        _testPlansDir = Path.Combine(Path.GetTempPath(), $"test-plans-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testPlansDir);
    }

    private string CreateTestPlan(string initialPrompt = "Test plan", string title = "Test Plan")
    {
        var planId = PlanYamlHelper.AllocatePlanId(_testPlansDir);
        var safeTitle = PlanYamlHelper.ToSafeTitle(title);
        var planFolder = Path.Combine(_testPlansDir, $"{planId}-{safeTitle}");
        Directory.CreateDirectory(planFolder);
        Directory.CreateDirectory(Path.Combine(planFolder, "Revisions"));

        var repoDir = Path.Combine(planFolder, "repo");
        Directory.CreateDirectory(repoDir);

        var plan = new PlanYaml
        {
            State = "Draft",
            Project = "Test",
            Level = "NiceToHave",
            Title = title,
            Repos = [repoDir],
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            InitialPrompt = initialPrompt,
            Prs = [],
            Commits = [],
            Verifications = [],
            RelatedPlans = [],
            DependsOn = []
        };

        PlanCommandHelpers.WritePlan(planFolder, plan, watcher: null);

        // Create initial revision
        var revisionPath = Path.Combine(planFolder, "Revisions", "001.md");
        File.WriteAllText(revisionPath, "# Test Plan\n\n## Problem\n\nTest problem");

        return planFolder;
    }

    [Fact]
    public void SetPlanStateByFolder_WithLargeInitialPrompt_DoesNotCorruptYaml()
    {
        // Arrange: Create a plan with a very long initialPrompt (>10KB)
        var largePrompt = new string('x', 50000);
        var planFolder = CreateTestPlan(initialPrompt: largePrompt);

        // Act: Change state multiple times
        PlanYamlHelper.SetPlanStateByFolder(planFolder, "Creating");
        PlanYamlHelper.SetPlanStateByFolder(planFolder, "Draft");

        // Assert: plan.yaml is valid and intact
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.NotNull(plan);
        Assert.Equal("Draft", plan.State);
        Assert.Equal(largePrompt, plan.InitialPrompt);

        // Verify the file can be parsed without errors
        var raw = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        var roundTrip = YamlHelper.Deserializer.Deserialize<PlanYaml>(raw);
        Assert.NotNull(roundTrip);
        Assert.Equal(largePrompt, roundTrip.InitialPrompt);

        // Cleanup
        Directory.Delete(planFolder, true);
    }

    [Fact]
    public void SetPlanStateByFolder_UsesAtomicWrite()
    {
        // Arrange: Create a plan
        var planFolder = CreateTestPlan();
        var planYamlPath = Path.Combine(planFolder, "plan.yaml");

        // Act: Change state
        var beforeModTime = File.GetLastWriteTimeUtc(planYamlPath);
        // Wait until the wall clock has advanced past the current mtime by more than the Windows
        // filesystem timestamp resolution (~15ms), so the next write is guaranteed a strictly newer
        // mtime. A fixed 10ms sleep was below that resolution and could leave mtimes equal.
        RetryHelper.WaitUntil(() => DateTime.UtcNow > beforeModTime.AddMilliseconds(20),
            TimeSpan.FromSeconds(2));
        PlanYamlHelper.SetPlanStateByFolder(planFolder, "Creating");

        // Assert: File was modified
        var afterModTime = File.GetLastWriteTimeUtc(planYamlPath);
        Assert.True(afterModTime > beforeModTime);

        // Verify no temporary files left behind
        var tempFiles = Directory.GetFiles(planFolder, "plan.yaml.tmp.*");
        Assert.Empty(tempFiles);

        // Verify content is valid
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.Equal("Creating", plan.State);

        // Cleanup
        Directory.Delete(planFolder, true);
    }

    [Fact]
    public void RepairPlanYaml_PreservesSchemaVersion()
    {
        // schemaVersion must survive the repair pass, otherwise the stamp gets stripped by the
        // structure normalizer and the plan is re-repaired on every startup (constraint #2).
        var malformed =
            "schemaVersion: 1\n" +
            "state: Draft\n" +
            "repos:\n" +
            "  - name: my-repo\n" +
            "    path: C:\\repos\\my-repo\n" +
            "    branch: main\n";

        var repaired = Ivy.Tendril.Services.Plans.PlanYamlRepairService.RepairPlanYaml(malformed);

        Assert.Matches(@"(?m)^schemaVersion:\s*1\s*$", repaired);
        var plan = YamlHelper.Deserializer.Deserialize<PlanYaml>(repaired);
        Assert.Equal(1, plan.SchemaVersion);
        Assert.Equal("Draft", plan.State);
    }

    [Fact]
    public void WritePlan_StampsCurrentSchemaVersion()
    {
        var planFolder = CreateTestPlan();

        var raw = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        Assert.Matches($@"(?m)^schemaVersion:\s*{PlanYaml.CurrentSchemaVersion}\s*$", raw);

        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.Equal(PlanYaml.CurrentSchemaVersion, plan.SchemaVersion);

        Directory.Delete(planFolder, true);
    }

    [Fact]
    public void SetPlanStateByFolder_UpdatesTimestamp()
    {
        // Arrange: Create a plan
        var planFolder = CreateTestPlan();
        var originalPlan = PlanCommandHelpers.ReadPlan(planFolder);
        var originalUpdated = originalPlan.Updated;

        // Act: Wait briefly then change state
        Thread.Sleep(100);
        PlanYamlHelper.SetPlanStateByFolder(planFolder, "Creating");

        // Assert: Updated timestamp changed
        var updatedPlan = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.True(updatedPlan.Updated > originalUpdated);

        // Cleanup
        Directory.Delete(planFolder, true);
    }
}
