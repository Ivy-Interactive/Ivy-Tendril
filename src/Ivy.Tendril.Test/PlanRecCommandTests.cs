using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class PlanRecCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _plansDir;
    private readonly string _originalTendrilHome;

    public PlanRecCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-rec-test-{Guid.NewGuid():N}");
        _plansDir = Path.Combine(_tempDir, "Plans");
        Directory.CreateDirectory(_plansDir);

        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, true); }
            catch { }
    }

    private void CreatePlan(string id, string title, List<RecommendationYaml>? recs = null)
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
            Recommendations = recs
        };

        var yaml = YamlHelper.Serializer.Serialize(plan);
        File.WriteAllText(Path.Combine(planDir, "plan.yaml"), yaml);
    }

    private PlanYaml ReadPlan(string planId)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
        return PlanCommandHelpers.ReadPlan(planFolder);
    }

    // --- ResolvePlanFolder ---

    [Fact]
    public void ResolvePlanFolder_FindsPlanById()
    {
        CreatePlan("10001", "FindTest");

        var folder = PlanCommandHelpers.ResolvePlanFolder("10001");

        Assert.EndsWith("10001-FindTest", folder.Replace('\\', '/').TrimEnd('/'));
    }

    [Fact]
    public void ResolvePlanFolder_NonExistentId_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            PlanCommandHelpers.ResolvePlanFolder("99999"));
    }

    // --- Add Recommendation ---

    [Fact]
    public void AddRecommendation_CreatesNewEntry()
    {
        CreatePlan("10010", "AddTest");

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10010");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        plan.Recommendations ??= [];
        plan.Recommendations.Add(new RecommendationYaml
        {
            Title = "New Rec",
            Description = "A new recommendation",
            State = "Pending",
            Impact = "High",
            Risk = "Medium"
        });
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10010");
        Assert.NotNull(result.Recommendations);
        Assert.Single(result.Recommendations);
        Assert.Equal("New Rec", result.Recommendations[0].Title);
        Assert.Equal("A new recommendation", result.Recommendations[0].Description);
        Assert.Equal("Pending", result.Recommendations[0].State);
        Assert.Equal("High", result.Recommendations[0].Impact);
        Assert.Equal("Medium", result.Recommendations[0].Risk);
    }

    [Fact]
    public void AddRecommendation_MultipleEntries()
    {
        CreatePlan("10011", "MultiAdd");

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10011");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations ??= [];

        plan.Recommendations.Add(new RecommendationYaml { Title = "First", Description = "Desc1" });
        plan.Recommendations.Add(new RecommendationYaml { Title = "Second", Description = "Desc2" });
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10011");
        Assert.Equal(2, result.Recommendations!.Count);
        Assert.Equal("First", result.Recommendations[0].Title);
        Assert.Equal("Second", result.Recommendations[1].Title);
    }

    [Fact]
    public void AddRecommendation_PreservesExisting()
    {
        CreatePlan("10012", "PreserveTest", [
            new RecommendationYaml { Title = "Existing", Description = "Already here", State = "Accepted" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10012");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations!.Add(new RecommendationYaml { Title = "New", Description = "Added" });
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10012");
        Assert.Equal(2, result.Recommendations!.Count);
        Assert.Equal("Existing", result.Recommendations[0].Title);
        Assert.Equal("Accepted", result.Recommendations[0].State);
        Assert.Equal("New", result.Recommendations[1].Title);
    }

    // --- Remove Recommendation ---

    [Fact]
    public void RemoveRecommendation_RemovesEntry()
    {
        CreatePlan("10020", "RemoveTest", [
            new RecommendationYaml { Title = "ToRemove", Description = "Will be removed" },
            new RecommendationYaml { Title = "ToKeep", Description = "Will stay" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10020");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        var match = plan.Recommendations!.First(r =>
            r.Title.Equals("ToRemove", StringComparison.OrdinalIgnoreCase));
        plan.Recommendations.Remove(match);
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10020");
        Assert.Single(result.Recommendations!);
        Assert.Equal("ToKeep", result.Recommendations[0].Title);
    }

    [Fact]
    public void RemoveRecommendation_LastEntry_LeavesEmptyList()
    {
        CreatePlan("10021", "RemoveLast", [
            new RecommendationYaml { Title = "Only", Description = "Alone" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10021");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations!.RemoveAt(0);
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10021");
        Assert.Empty(result.Recommendations!);
    }

    // --- Accept Recommendation ---

    [Fact]
    public void AcceptRecommendation_SetsState()
    {
        CreatePlan("10030", "AcceptTest", [
            new RecommendationYaml { Title = "Pending Rec", Description = "Waiting", State = "Pending" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10030");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations![0].State = "Accepted";
        plan.Recommendations[0].DeclineReason = null;
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10030");
        Assert.Equal("Accepted", result.Recommendations![0].State);
        Assert.Null(result.Recommendations[0].DeclineReason);
    }

    [Fact]
    public void AcceptWithNotes_SetsAcceptedWithNotesState()
    {
        CreatePlan("10031", "AcceptNotesTest", [
            new RecommendationYaml { Title = "Pending Rec", Description = "Waiting", State = "Pending" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10031");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations![0].State = "AcceptedWithNotes";
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10031");
        Assert.Equal("AcceptedWithNotes", result.Recommendations![0].State);
    }

    // --- Decline Recommendation ---

    [Fact]
    public void DeclineRecommendation_SetsStateAndReason()
    {
        CreatePlan("10040", "DeclineTest", [
            new RecommendationYaml { Title = "Pending Rec", Description = "Waiting", State = "Pending" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10040");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations![0].State = "Declined";
        plan.Recommendations[0].DeclineReason = "Not relevant";
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10040");
        Assert.Equal("Declined", result.Recommendations![0].State);
        Assert.Equal("Not relevant", result.Recommendations[0].DeclineReason);
    }

    [Fact]
    public void DeclineRecommendation_WithoutReason_NullReason()
    {
        CreatePlan("10041", "DeclineNoReason", [
            new RecommendationYaml { Title = "Pending Rec", Description = "Waiting", State = "Pending" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10041");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations![0].State = "Declined";
        plan.Recommendations[0].DeclineReason = null;
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10041");
        Assert.Equal("Declined", result.Recommendations![0].State);
        Assert.Null(result.Recommendations[0].DeclineReason);
    }

    // --- Set Recommendation Fields ---

    [Fact]
    public void SetRecommendation_UpdateTitle()
    {
        CreatePlan("10050", "SetTitleTest", [
            new RecommendationYaml { Title = "Original", Description = "Desc" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10050");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations![0].Title = "Renamed";
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10050");
        Assert.Equal("Renamed", result.Recommendations![0].Title);
    }

    [Fact]
    public void SetRecommendation_UpdateDescription()
    {
        CreatePlan("10051", "SetDescTest", [
            new RecommendationYaml { Title = "Rec1", Description = "Old desc" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10051");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations![0].Description = "New desc";
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10051");
        Assert.Equal("New desc", result.Recommendations![0].Description);
    }

    [Fact]
    public void SetRecommendation_UpdateImpact()
    {
        CreatePlan("10052", "SetImpactTest", [
            new RecommendationYaml { Title = "Rec1", Description = "Desc", Impact = "Small" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10052");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations![0].Impact = "High";
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10052");
        Assert.Equal("High", result.Recommendations![0].Impact);
    }

    [Fact]
    public void SetRecommendation_UpdateRisk()
    {
        CreatePlan("10053", "SetRiskTest", [
            new RecommendationYaml { Title = "Rec1", Description = "Desc", Risk = "High" }
        ]);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10053");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations![0].Risk = "Small";
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var result = ReadPlan("10053");
        Assert.Equal("Small", result.Recommendations![0].Risk);
    }

    // --- Roundtrip/Serialization ---

    [Fact]
    public void Recommendations_SurviveRoundtrip()
    {
        CreatePlan("10060", "RoundtripTest", [
            new RecommendationYaml
            {
                Title = "Full Rec",
                Description = "Complete recommendation",
                State = "Declined",
                DeclineReason = "Too risky",
                Impact = "High",
                Risk = "High"
            }
        ]);

        var result = ReadPlan("10060");
        Assert.NotNull(result.Recommendations);
        Assert.Single(result.Recommendations);
        var rec = result.Recommendations[0];
        Assert.Equal("Full Rec", rec.Title);
        Assert.Equal("Complete recommendation", rec.Description);
        Assert.Equal("Declined", rec.State);
        Assert.Equal("Too risky", rec.DeclineReason);
        Assert.Equal("High", rec.Impact);
        Assert.Equal("High", rec.Risk);
    }

    [Fact]
    public void NullRecommendations_DeserializesAsNull()
    {
        CreatePlan("10061", "NullRecsTest");

        var result = ReadPlan("10061");
        Assert.Null(result.Recommendations);
    }

    [Fact]
    public void EmptyRecommendationsList_DeserializesAsEmpty()
    {
        var planDir = Path.Combine(_plansDir, "10062-EmptyListTest");
        Directory.CreateDirectory(planDir);
        var plan = new PlanYaml
        {
            State = "Draft",
            Project = "Test",
            Title = "EmptyListTest",
            Repos = [_tempDir],
            Recommendations = []
        };
        var yaml = YamlHelper.Serializer.Serialize(plan);
        File.WriteAllText(Path.Combine(planDir, "plan.yaml"), yaml);

        var result = ReadPlan("10062");
        Assert.NotNull(result.Recommendations);
        Assert.Empty(result.Recommendations);
    }

    // --- Timestamp Updates ---

    [Fact]
    public void ModifyingRecommendation_CanUpdateTimestamp()
    {
        CreatePlan("10070", "TimestampTest");
        var before = ReadPlan("10070").Updated;

        Thread.Sleep(50);

        var planFolder = PlanCommandHelpers.ResolvePlanFolder("10070");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        plan.Recommendations ??= [];
        plan.Recommendations.Add(new RecommendationYaml { Title = "New", Description = "Test" });
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan);

        var after = ReadPlan("10070").Updated;
        Assert.True(after > before);
    }

    // --- Edge Cases ---

    [Fact]
    public void RecommendationWithSpecialCharacters_SurvivesRoundtrip()
    {
        CreatePlan("10080", "SpecialCharsTest", [
            new RecommendationYaml
            {
                Title = "Fix: \"quotes\" & <brackets>",
                Description = "Line1\nLine2\nLine3",
                State = "Pending"
            }
        ]);

        var result = ReadPlan("10080");
        Assert.Equal("Fix: \"quotes\" & <brackets>", result.Recommendations![0].Title);
        Assert.Contains("Line1", result.Recommendations[0].Description);
    }
}
