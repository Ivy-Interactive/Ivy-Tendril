using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceEventSplitTests
{
    private static JobService CreateService()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void CompleteJob_RaisesJobsStructureChanged()
    {
        var service = CreateService();
        var id = service.CreateTestJob("ExecutePlan", "test-plan");

        var structureChangedFired = false;
        var propertyChangedFired = false;
        service.JobsStructureChanged += () => structureChangedFired = true;
        service.JobPropertyChanged += () => propertyChangedFired = true;

        service.CompleteJob(id, 0);

        Assert.True(structureChangedFired);
        Assert.False(propertyChangedFired);
    }

    [Fact]
    public void CompleteJob_AlsoRaisesJobsChanged_ForBackwardCompatibility()
    {
        var service = CreateService();
        var id = service.CreateTestJob("ExecutePlan", "test-plan");

        var jobsChangedFired = false;
        service.JobsChanged += () => jobsChangedFired = true;

        service.CompleteJob(id, 0);

        Assert.True(jobsChangedFired);
    }

    [Fact]
    public void DeleteJob_RaisesJobsStructureChanged()
    {
        var service = CreateService();
        var id = service.CreateTestJob("ExecutePlan", "test-plan");
        service.CompleteJob(id, 0);

        var structureChangedFired = false;
        var propertyChangedFired = false;
        service.JobsStructureChanged += () => structureChangedFired = true;
        service.JobPropertyChanged += () => propertyChangedFired = true;

        service.DeleteJob(id);

        Assert.True(structureChangedFired);
        Assert.False(propertyChangedFired);
    }

    [Fact]
    public void StopJob_RaisesJobsStructureChanged()
    {
        var service = CreateService();
        var id = service.CreateTestJob("ExecutePlan", "test-plan");

        var structureChangedFired = false;
        var propertyChangedFired = false;
        service.JobsStructureChanged += () => structureChangedFired = true;
        service.JobPropertyChanged += () => propertyChangedFired = true;

        service.StopJob(id);

        Assert.True(structureChangedFired);
        Assert.False(propertyChangedFired);
    }

    [Fact]
    public void ClearCompletedJobs_RaisesJobsStructureChanged()
    {
        var service = CreateService();
        var id = service.CreateTestJob("ExecutePlan", "test-plan");
        service.CompleteJob(id, 0);

        var structureChangedFired = false;
        service.JobsStructureChanged += () => structureChangedFired = true;

        service.ClearCompletedJobs();

        Assert.True(structureChangedFired);
    }

    [Fact]
    public void ClearFailedJobs_RaisesJobsStructureChanged()
    {
        var service = CreateService();
        var id = service.CreateTestJob("ExecutePlan", "test-plan");
        service.CompleteJob(id, 1);

        var structureChangedFired = false;
        service.JobsStructureChanged += () => structureChangedFired = true;

        service.ClearFailedJobs();

        Assert.True(structureChangedFired);
    }
}
