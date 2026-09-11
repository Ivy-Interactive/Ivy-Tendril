using System;
using System.IO;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test;

public class JobServiceCreatePlanVerificationTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _plansDir;
    private readonly string _promptsRoot;

    public JobServiceCreatePlanVerificationTests()
    {
        _plansDir = Path.Combine(_tempDir.Path, "Plans");
        _promptsRoot = Path.Combine(_tempDir.Path, "Prompts");
        Directory.CreateDirectory(_plansDir);
        Directory.CreateDirectory(_promptsRoot);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public void VerifyCreatePlanResult_FailsAndCleansUpWhenNoRevisionsWritten()
    {
        var planFolder = Path.Combine(_plansDir, "00100-EmptyPlan");
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), "state: Draft\ntitle: Empty Plan\n");

        var planReader = new FakePlanReaderService { PlansDirectory = _plansDir };
        var handler = new JobCompletionHandler(
            configService: null,
            logger: NullLogger.Instance,
            modelPricingService: null,
            planReaderService: planReader,
            telemetryService: null,
            planWatcherService: null,
            promptsRoot: _promptsRoot
        );

        var job = new JobItem
        {
            Id = "00001",
            Type = "CreatePlan",
            AllocatedPlanId = "00100",
            ReportedPlanId = "00100",
            PlanFile = "00100-EmptyPlan",
            Status = JobStatus.Completed
        };

        handler.VerifyCreatePlanResult(job);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.False(Directory.Exists(planFolder));
    }
}
