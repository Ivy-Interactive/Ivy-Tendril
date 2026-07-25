using Ivy.Tendril.Commands;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test.Commands;

[Collection("TendrilHome")]
public class JobStatusCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-job-status-test");
    private readonly string _originalTendrilHome;

    public JobStatusCommandTests()
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
            config.AddCommand<JobStatusCommand>("status");
        });
        return app;
    }

    [Fact]
    public void Execute_NoMasterFileRunning_ReturnsZeroAndWarnsOnStderr()
    {
        // No .master file exists under TENDRIL_HOME, so Discover() throws. The command must
        // treat this as best-effort telemetry rather than failing the caller's script.
        var app = BuildApp();
        var originalError = Console.Error;
        var errorWriter = new StringWriter();
        Console.SetError(errorWriter);

        int exit;
        try
        {
            exit = app.Run(["status", "00432", "--message", "Running verifications..."]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(0, exit);
        Assert.Contains("Warning:", errorWriter.ToString());
        Assert.Contains("00432", errorWriter.ToString());
    }
}
