using Ivy.Tendril.Commands;

namespace Ivy.Tendril.Test.Commands;

public class JobFailCommandTests
{
    [Fact]
    public void Validate_MissingMessage_Fails()
    {
        var settings = new JobFailSettings { JobId = "00001", Message = "" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_MissingJobId_Fails()
    {
        var settings = new JobFailSettings { JobId = "", Message = "Worktree creation failed" };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_JobIdAndMessage_Succeeds()
    {
        var settings = new JobFailSettings { JobId = "00001", Message = "Worktree creation failed" };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }
}
