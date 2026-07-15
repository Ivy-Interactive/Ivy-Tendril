using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class PlanReaderServiceRecoveryTests : IDisposable
{
    private readonly PlanReaderService _service;
    private readonly string _tempDir;

    public PlanReaderServiceRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        var settings = new TendrilSettings();
        var configService = new ConfigService(settings, _tempDir);
        _service = new PlanReaderService(configService, NullLogger<PlanReaderService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void CreatePlan(string folderName, string state)
    {
        var dir = Path.Combine(_service.PlansDirectory, folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plan.yaml"),
            $"state: {state}\nproject: Test\ntitle: Test Plan\nupdated: 2026-01-01T00:00:00Z\n");
    }

    private string ReadState(string folderName)
    {
        var yaml = File.ReadAllText(Path.Combine(_service.PlansDirectory, folderName, "plan.yaml"));
        var match = Regex.Match(yaml, @"(?m)^state:\s*(.+)$");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private string ReadRaw(string folderName) =>
        File.ReadAllText(Path.Combine(_service.PlansDirectory, folderName, "plan.yaml"));

    private int? ReadSchemaVersion(string folderName)
    {
        var match = Regex.Match(ReadRaw(folderName), @"(?m)^schemaVersion:\s*(\d+)\s*$");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    [Fact]
    public void Executing_Plans_Are_Recovered_To_Failed()
    {
        CreatePlan("01099-TestPlan", "Executing");

        _service.RecoverStuckPlans();

        Assert.Equal("Failed", ReadState("01099-TestPlan"));
    }

    [Fact]
    public void Building_Plans_Are_Recovered_To_Draft()
    {
        CreatePlan("01100-BuildPlan", "Creating");

        _service.RecoverStuckPlans();

        Assert.Equal("Draft", ReadState("01100-BuildPlan"));
    }

    [Fact]
    public void Updating_Plans_Are_Recovered_To_Draft()
    {
        CreatePlan("01101-UpdatePlan", "Updating");

        _service.RecoverStuckPlans();

        Assert.Equal("Draft", ReadState("01101-UpdatePlan"));
    }

    [Fact]
    public void Draft_Plans_Are_Not_Changed()
    {
        CreatePlan("01102-DraftPlan", "Draft");

        _service.RecoverStuckPlans();

        Assert.Equal("Draft", ReadState("01102-DraftPlan"));
    }

    [Fact]
    public void Failed_Plans_Are_Not_Changed()
    {
        CreatePlan("01103-FailedPlan", "Failed");

        _service.RecoverStuckPlans();

        Assert.Equal("Failed", ReadState("01103-FailedPlan"));
    }

    [Fact]
    public void One_Bad_Plan_Does_Not_Block_Others()
    {
        // Create a plan with an unreadable plan.yaml (directory instead of file)
        var badDir = Path.Combine(_service.PlansDirectory, "01104-BadPlan");
        Directory.CreateDirectory(badDir);
        Directory.CreateDirectory(Path.Combine(badDir, "plan.yaml")); // directory, not file

        CreatePlan("01105-GoodPlan", "Executing");

        _service.RecoverStuckPlans();

        Assert.Equal("Failed", ReadState("01105-GoodPlan"));
    }

    [Fact]
    public void Multiple_Stuck_Plans_Are_All_Recovered()
    {
        CreatePlan("01200-Plan1", "Executing");
        CreatePlan("01201-Plan2", "Creating");
        CreatePlan("01202-Plan3", "Updating");
        CreatePlan("01203-Plan4", "Draft");
        CreatePlan("01204-Plan5", "Failed");

        _service.RecoverStuckPlans();

        Assert.Equal("Failed", ReadState("01200-Plan1"));
        Assert.Equal("Draft", ReadState("01201-Plan2"));
        Assert.Equal("Draft", ReadState("01202-Plan3"));
        Assert.Equal("Draft", ReadState("01203-Plan4"));
        Assert.Equal("Failed", ReadState("01204-Plan5"));
    }

    [Fact]
    public void MigratePlans_RewritesLegacyStateNames()
    {
        CreatePlan("01300-Legacy1", "Building");
        CreatePlan("01301-Legacy2", "ReadyForReview");

        _service.MigratePlans();

        Assert.Equal("Creating", ReadState("01300-Legacy1"));
        Assert.Equal("Review", ReadState("01301-Legacy2"));
    }

    [Fact]
    public void MigratePlans_LeavesCurrentStatesUntouched_AndIsIdempotent()
    {
        CreatePlan("01302-Current", "Draft");
        CreatePlan("01303-Migrated", "Building");

        _service.MigratePlans();
        _service.MigratePlans(); // re-running is a no-op (gated by stamped schemaVersion)

        Assert.Equal("Draft", ReadState("01302-Current"));
        Assert.Equal("Creating", ReadState("01303-Migrated"));
    }

    [Fact]
    public void MigratePlans_StampsCurrentSchemaVersion_OnLegacyPlan()
    {
        // CreatePlan writes well-formed YAML with no schemaVersion (version 0). The structural repair is
        // a no-op here, but we must still stamp the version so later startups skip the plan (constraint #3).
        CreatePlan("01400-Legacy", "Draft");
        Assert.Null(ReadSchemaVersion("01400-Legacy"));

        _service.MigratePlans();

        Assert.Equal(PlanYaml.CurrentSchemaVersion, ReadSchemaVersion("01400-Legacy"));
        // The stamped file must still deserialize cleanly.
        var plan = YamlHelper.Deserializer.Deserialize<PlanYaml>(ReadRaw("01400-Legacy"));
        Assert.Equal("Draft", plan.State);
    }

    [Fact]
    public void MigratePlans_SkipsPlanAlreadyAtCurrentVersion()
    {
        CreatePlan("01401-Current", "Draft");
        _service.MigratePlans(); // first pass stamps the version
        var afterFirst = ReadRaw("01401-Current");
        Assert.Equal(PlanYaml.CurrentSchemaVersion, ReadSchemaVersion("01401-Current"));

        _service.MigratePlans(); // second pass must be a no-op (gated by schemaVersion)

        Assert.Equal(afterFirst, ReadRaw("01401-Current"));
    }

    [Fact]
    public void MigratePlans_LeavesTerminalPlanUntouched()
    {
        CreatePlan("01402-Done", "Completed");
        var before = ReadRaw("01402-Done");

        _service.MigratePlans();

        // Terminal plans are skipped entirely, so they are never rewritten or stamped.
        Assert.Equal(before, ReadRaw("01402-Done"));
        Assert.Null(ReadSchemaVersion("01402-Done"));
    }
}
