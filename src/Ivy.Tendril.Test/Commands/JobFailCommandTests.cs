using Ivy.Tendril.Commands;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test.Commands;

[Collection("TendrilHome")]
public class JobFailCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-job-fail-test");
    private readonly string _originalTendrilHome;

    public JobFailCommandTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private static CommandApp BuildApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddCommand<JobFailCommand>("fail");
        });
        return app;
    }

    [Fact]
    public void Execute_NoMasterFileRunning_ReturnsZeroAndWarnsOnStderr()
    {
        // No .master file exists under TENDRIL_HOME, so Discover() throws. Recording the
        // failure reason is best effort; the caller's own non-zero exit is what actually
        // marks the job failed, so a dropped report must not fail this command too.
        var app = BuildApp();
        var originalError = Console.Error;
        var errorWriter = new StringWriter();
        Console.SetError(errorWriter);

        int exit;
        try
        {
            exit = app.Run(["fail", "00432", "--message", "Worktree creation failed"]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(0, exit);
        Assert.Contains("Warning:", errorWriter.ToString());
        Assert.Contains("00432", errorWriter.ToString());
    }

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
