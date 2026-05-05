using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

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
        Directory.CreateDirectory(Path.Combine(planFolder, "revisions"));

        var plan = new PlanYaml
        {
            State = "Draft",
            Project = "Test",
            Level = "NiceToHave",
            Title = title,
            Repos = [],
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
        var revisionPath = Path.Combine(planFolder, "revisions", "001.md");
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
        PlanYamlHelper.SetPlanStateByFolder(planFolder, "Building");
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
    public async Task ConcurrentStateChanges_DoNotCorruptPlanYaml()
    {
        // Arrange: Create a plan
        var planFolder = CreateTestPlan();

        // Act: Rapidly change state multiple times concurrently
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await Task.Delay(Random.Shared.Next(0, 50));
            var state = i % 2 == 0 ? "Draft" : "Building";
            PlanYamlHelper.SetPlanStateByFolder(planFolder, state);
        });

        await Task.WhenAll(tasks);

        // Assert: plan.yaml is still valid and can be read
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.NotNull(plan);
        Assert.Contains(plan.State, new[] { "Draft", "Building" });

        // Verify no corruption - file should still be valid YAML
        var raw = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
        var roundTrip = YamlHelper.Deserializer.Deserialize<PlanYaml>(raw);
        Assert.NotNull(roundTrip);
        Assert.NotNull(roundTrip.State);
        Assert.NotNull(roundTrip.Title);

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
        Thread.Sleep(10); // Ensure timestamp difference
        PlanYamlHelper.SetPlanStateByFolder(planFolder, "Building");

        // Assert: File was modified
        var afterModTime = File.GetLastWriteTimeUtc(planYamlPath);
        Assert.True(afterModTime > beforeModTime);

        // Verify no temporary files left behind
        var tempFiles = Directory.GetFiles(planFolder, "plan.yaml.tmp.*");
        Assert.Empty(tempFiles);

        // Verify content is valid
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.Equal("Building", plan.State);

        // Cleanup
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
        PlanYamlHelper.SetPlanStateByFolder(planFolder, "Building");

        // Assert: Updated timestamp changed
        var updatedPlan = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.True(updatedPlan.Updated > originalUpdated);

        // Cleanup
        Directory.Delete(planFolder, true);
    }
}
