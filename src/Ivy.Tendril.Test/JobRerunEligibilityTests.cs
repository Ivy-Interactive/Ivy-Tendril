using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Xunit;

namespace Ivy.Tendril.Test;

public class JobRerunEligibilityTests
{
    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Blocked)]
    public void CanRerun_CompletedRetryPlanArgs_ReturnsTrue(JobStatus status)
    {
        var job = new JobItem
        {
            Id = "test-id",
            Status = status,
            TypedArgs = new RetryPlanArgs("/plans/00001-Test", "change request")
        };

        var result = JobsApp.CanRerun(job);

        if (status == JobStatus.Completed)
            Assert.True(result);
        else
            Assert.False(result);
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Blocked)]
    public void CanRerun_CompletedExecutePlanArgs_ReturnsTrue(JobStatus status)
    {
        var job = new JobItem
        {
            Id = "test-id",
            Status = status,
            TypedArgs = new ExecutePlanArgs("/plans/00001-Test")
        };

        var result = JobsApp.CanRerun(job);

        if (status == JobStatus.Completed)
            Assert.True(result);
        else
            Assert.False(result);
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Blocked)]
    public void CanRerun_CompletedUpdatePlanArgs_ReturnsTrue(JobStatus status)
    {
        var job = new JobItem
        {
            Id = "test-id",
            Status = status,
            TypedArgs = new UpdatePlanArgs("/plans/00001-Test")
        };

        var result = JobsApp.CanRerun(job);

        if (status == JobStatus.Completed)
            Assert.True(result);
        else
            Assert.False(result);
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Blocked)]
    public void CanRerun_CompletedCreatePrArgs_ReturnsFalse(JobStatus status)
    {
        var job = new JobItem
        {
            Id = "test-id",
            Status = status,
            TypedArgs = new CreatePrArgs("/plans/00001-Test")
        };

        var result = JobsApp.CanRerun(job);

        Assert.False(result);
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Blocked)]
    public void CanRerun_CompletedExpandPlanArgs_ReturnsFalse(JobStatus status)
    {
        var job = new JobItem
        {
            Id = "test-id",
            Status = status,
            TypedArgs = new ExpandPlanArgs("/plans/00001-Test")
        };

        var result = JobsApp.CanRerun(job);

        Assert.False(result);
    }

    [Theory]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Timeout)]
    [InlineData(JobStatus.Stopped)]
    public void CanRerun_FailedStateCreatePrArgs_ReturnsTrue(JobStatus status)
    {
        var job = new JobItem
        {
            Id = "test-id",
            Status = status,
            TypedArgs = new CreatePrArgs("/plans/00001-Test")
        };

        var result = JobsApp.CanRerun(job);

        Assert.True(result);
    }

    [Theory]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Timeout)]
    [InlineData(JobStatus.Stopped)]
    public void CanRerun_FailedStateExpandPlanArgs_ReturnsTrue(JobStatus status)
    {
        var job = new JobItem
        {
            Id = "test-id",
            Status = status,
            TypedArgs = new ExpandPlanArgs("/plans/00001-Test")
        };

        var result = JobsApp.CanRerun(job);

        Assert.True(result);
    }

    [Fact]
    public void CanRerun_NullJob_ReturnsFalse()
    {
        var result = JobsApp.CanRerun(null);

        Assert.False(result);
    }
}
