using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Telemetry;
using Xunit;

namespace Ivy.Tendril.Test.Services;

public class TelemetryPlanUuidTests
{
    private const string AnonId = "6f1c9c1e-3a5e-4d1a-9d0f-2a6c4b8e1d33";
    private const string OtherAnonId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void DerivePlanUuid_IsStableForTheSameInstallAndPlan()
    {
        var first = TelemetryService.DerivePlanUuid(AnonId, "00042");
        var second = TelemetryService.DerivePlanUuid(AnonId, "00042");

        Assert.Equal(first, second);
    }

    [Fact]
    public void DerivePlanUuid_NormalizesIntAndZeroPaddedForms()
    {
        // PlanReaderService supplies the int form from the database; JobCompletionHandler
        // supplies the zero-padded form parsed off the folder name. Both must group together.
        Assert.Equal(
            TelemetryService.DerivePlanUuid(AnonId, "00042"),
            TelemetryService.DerivePlanUuid(AnonId, "42"));
    }

    [Fact]
    public void DerivePlanUuid_DiffersAcrossPlans()
    {
        Assert.NotEqual(
            TelemetryService.DerivePlanUuid(AnonId, "00042"),
            TelemetryService.DerivePlanUuid(AnonId, "00043"));
    }

    [Fact]
    public void DerivePlanUuid_DiffersAcrossInstallsForTheSamePlanId()
    {
        // The whole point: plan ids are a per-install counter, so "00042" exists everywhere.
        Assert.NotEqual(
            TelemetryService.DerivePlanUuid(AnonId, "00042"),
            TelemetryService.DerivePlanUuid(OtherAnonId, "00042"));
    }

    [Fact]
    public void DerivePlanUuid_ReturnsCanonicalUuidWithVersionAndVariantBits()
    {
        var uuid = TelemetryService.DerivePlanUuid(AnonId, "00042");

        Assert.NotNull(uuid);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-8[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", uuid);
        Assert.True(Guid.TryParse(uuid, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DerivePlanUuid_ReturnsNullWithoutAPlan(string? planId)
    {
        Assert.Null(TelemetryService.DerivePlanUuid(AnonId, planId));
    }

    [Fact]
    public void DerivePlanUuid_ReturnsNullWhenTelemetryIsDisabled()
    {
        // A disabled TelemetryService has an empty distinct id.
        Assert.Null(TelemetryService.DerivePlanUuid("", "00042"));
    }

    [Fact]
    public void ResolvePlanId_ReadsThePlanIdPrefixForPlanScopedJobs()
    {
        var job = new JobItem { Type = "ExecutePlan", PlanFile = "00042-SomeTitle" };

        Assert.Equal("00042", job.ResolvePlanId());
    }

    [Fact]
    public void ResolvePlanId_FallsBackToReportedIdWhileCreatePlanStillHoldsADescription()
    {
        // A CreatePlan job starts with the task description in PlanFile and only acquires a
        // folder name once VerifyCreatePlanResult runs, so the prefix parse must not match it.
        var job = new JobItem
        {
            Type = "CreatePlan",
            PlanFile = "Add a settings toggle",
            ReportedPlanId = "00042"
        };

        Assert.Equal("00042", job.ResolvePlanId());
    }

    [Fact]
    public void ResolvePlanId_YieldsNoPlanUuidForJobsWithNoPlan()
    {
        var job = new JobItem { Type = "CreatePlan", PlanFile = "Add a settings toggle" };

        Assert.Equal("", job.ResolvePlanId());
        Assert.Null(TelemetryService.DerivePlanUuid(AnonId, job.ResolvePlanId()));
    }
}
