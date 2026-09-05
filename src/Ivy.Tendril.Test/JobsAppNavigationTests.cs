using Ivy.Tendril.Apps.Jobs;
using Xunit;

namespace Ivy.Tendril.Test;

public class JobsAppNavigationTests
{
    [Fact]
    public void JobsAppArgs_DefaultsToNullJobId()
    {
        var args = new JobsAppArgs();
        Assert.Null(args.JobId);
    }

    [Fact]
    public void JobsAppArgs_PreservesProvidedJobId()
    {
        var args = new JobsAppArgs("00042");
        Assert.Equal("00042", args.JobId);
    }

    [Fact]
    public void JobsAppArgs_EqualityWorks()
    {
        var args1 = new JobsAppArgs("00042");
        var args2 = new JobsAppArgs("00042");
        var args3 = new JobsAppArgs("00043");

        Assert.Equal(args1, args2);
        Assert.NotEqual(args1, args3);
    }
}
