using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class PlanVerificationCommandTests : IDisposable
{
    private readonly string _originalTendrilHome;
    private readonly string? _originalTendrilPlans;
    private readonly string _plansDir;
    private readonly string _tempDir;

    public PlanVerificationCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-planver-test-{Guid.NewGuid():N}");
        _plansDir = Path.Combine(_tempDir, "Plans");
        Directory.CreateDirectory(_plansDir);

        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalTendrilPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", _originalTendrilPlans);
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void CreatePlan(string id, string title, List<PlanVerificationEntry>? verifications = null)
    {
        var folderName = $"{id}-{title}";
        var planDir = Path.Combine(_plansDir, folderName);
        Directory.CreateDirectory(planDir);

        var plan = new PlanYaml
        {
            State = "Draft",
            Project = "TestProject",
            Title = title,
            Repos = [_tempDir],
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            Verifications = verifications ?? []
        };

        var yaml = YamlHelper.Serializer.Serialize(plan);
        File.WriteAllText(Path.Combine(planDir, "plan.yaml"), yaml);
    }

    private PlanYaml ReadPlan(string planId)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
        return PlanCommandHelpers.ReadPlan(planFolder);
    }

    // --- Add Verification ---

    [Fact]
    public void AddVerification_CreatesNewEntry()
    {
        CreatePlan("30001", "AddVerTest");

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("30001");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        plan.Verifications.Add(new PlanVerificationEntry { Name = "UnitTests", Status = "Pending" });
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("30001");
        Assert.Single(result.Verifications);
        Assert.Equal("UnitTests", result.Verifications[0].Name);
        Assert.Equal("Pending", result.Verifications[0].Status);
    }

    [Fact]
    public void AddVerification_MultipleEntries()
    {
        CreatePlan("30002", "MultiVerTest");

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("30002");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Verifications.Add(new PlanVerificationEntry { Name = "UnitTests", Status = "Pending" });
        plan.Verifications.Add(new PlanVerificationEntry { Name = "Integration", Status = "Pass" });
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("30002");
        Assert.Equal(2, result.Verifications.Count);
        Assert.Equal("UnitTests", result.Verifications[0].Name);
        Assert.Equal("Integration", result.Verifications[1].Name);
    }

    [Fact]
    public void AddVerification_PreservesExisting()
    {
        CreatePlan("30003", "PreserveVerTest", [
            new PlanVerificationEntry { Name = "Existing", Status = "Pass" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("30003");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Verifications.Add(new PlanVerificationEntry { Name = "New", Status = "Pending" });
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("30003");
        Assert.Equal(2, result.Verifications.Count);
        Assert.Equal("Existing", result.Verifications[0].Name);
        Assert.Equal("Pass", result.Verifications[0].Status);
        Assert.Equal("New", result.Verifications[1].Name);
    }

    // --- Remove Verification ---

    [Fact]
    public void RemoveVerification_RemovesEntry()
    {
        CreatePlan("30010", "RemoveVerTest", [
            new PlanVerificationEntry { Name = "ToRemove", Status = "Pending" },
            new PlanVerificationEntry { Name = "ToKeep", Status = "Pass" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("30010");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        var match = plan.Verifications.First(v =>
            v.Name.Equals("ToRemove", StringComparison.OrdinalIgnoreCase));
        plan.Verifications.Remove(match);
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("30010");
        Assert.Single(result.Verifications);
        Assert.Equal("ToKeep", result.Verifications[0].Name);
    }

    [Fact]
    public void RemoveVerification_LastEntry_LeavesEmptyList()
    {
        CreatePlan("30011", "RemoveLastVer", [
            new PlanVerificationEntry { Name = "Only", Status = "Fail" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("30011");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Verifications.RemoveAt(0);
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("30011");
        Assert.Empty(result.Verifications);
    }

    // --- Set Verification Status ---

    [Fact]
    public void SetVerificationStatus_UpdatesExisting()
    {
        CreatePlan("30020", "SetStatusTest", [
            new PlanVerificationEntry { Name = "UnitTests", Status = "Pending" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("30020");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Verifications[0].Status = "Pass";
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("30020");
        Assert.Equal("Pass", result.Verifications[0].Status);
    }

    [Fact]
    public void SetVerificationStatus_AllStatuses()
    {
        foreach (var status in new[] { "Pass", "Fail", "Pending", "Skipped" })
        {
            var id = $"3003{Array.IndexOf(new[] { "Pass", "Fail", "Pending", "Skipped" }, status)}";
            CreatePlan(id, $"Status{status}", [
                new PlanVerificationEntry { Name = "Test", Status = "Pending" }
            ]);

            var planFolder = PlanCommandHelpers.ResolvePlanFolder(id);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            plan.Verifications[0].Status = status;
            PlanCommandHelpers.WritePlan(planFolder, plan);

            var result = ReadPlan(id);
            Assert.Equal(status, result.Verifications[0].Status);
        }
    }

    // --- Timestamp ---

    [Fact]
    public void ModifyingVerification_UpdatesTimestamp()
    {
        CreatePlan("30040", "TimestampTest");
        var before = ReadPlan("30040").Updated;

        Thread.Sleep(50);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("30040");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Verifications.Add(new PlanVerificationEntry { Name = "New", Status = "Pending" });
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var after = ReadPlan("30040").Updated;
        Assert.True(after > before);
    }

    // --- Roundtrip ---

    [Fact]
    public void Verifications_SurviveRoundtrip()
    {
        CreatePlan("30050", "RoundtripTest", [
            new PlanVerificationEntry { Name = "UnitTests", Status = "Pass" },
            new PlanVerificationEntry { Name = "Integration", Status = "Fail" },
            new PlanVerificationEntry { Name = "E2E", Status = "Skipped" }
        ]);

        var result = ReadPlan("30050");
        Assert.Equal(3, result.Verifications.Count);
        Assert.Equal("UnitTests", result.Verifications[0].Name);
        Assert.Equal("Pass", result.Verifications[0].Status);
        Assert.Equal("Integration", result.Verifications[1].Name);
        Assert.Equal("Fail", result.Verifications[1].Status);
        Assert.Equal("E2E", result.Verifications[2].Name);
        Assert.Equal("Skipped", result.Verifications[2].Status);
    }
}
