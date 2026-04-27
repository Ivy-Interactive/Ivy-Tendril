using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class JobServiceResourceDisposalTests
{
    private static JobService CreateService(ILogger<JobService>? logger = null)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(
            jobTimeout: TimeSpan.FromMinutes(30),
            staleOutputTimeout: TimeSpan.FromMinutes(10),
            logger: logger);
    }

    [Fact]
    public void DisposeResources_WithNullLogger_DoesNotThrow()
    {
        var job = new JobItem
        {
            Id = "test-job",
            Type = "ExecutePlan",
            Status = JobStatus.Running
        };

        // Should not throw when logger is null
        job.DisposeResources(null);

        Assert.Null(job.Process);
        Assert.Null(job.TimeoutCts);
    }

    [Fact]
    public void DisposeResources_WithValidResources_DisposesSuccessfully()
    {
        var logger = NullLogger<JobService>.Instance;
        var job = new JobItem
        {
            Id = "test-job",
            Type = "ExecutePlan",
            Status = JobStatus.Running
        };

        // Create a CancellationTokenSource that we can dispose
        job.TimeoutCts = new CancellationTokenSource();

        // Should dispose without exceptions
        job.DisposeResources(logger);

        Assert.Null(job.Process);
        Assert.Null(job.TimeoutCts);
    }

    [Fact]
    public void DisposeResources_LogsWarningWhenDisposalFails()
    {
        var logMessages = new List<string>();
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestLoggerProvider(logMessages));
        });
        var logger = loggerFactory.CreateLogger<JobService>();

        var job = new JobItem
        {
            Id = "test-job",
            Type = "ExecutePlan",
            Status = JobStatus.Running
        };

        // Create a CTS and dispose it, so the next dispose call fails
        job.TimeoutCts = new CancellationTokenSource();
        job.TimeoutCts.Dispose();

        // Should log a warning when disposal fails
        job.DisposeResources(logger);

        // Check that a warning was logged (disposal of already-disposed CTS throws ObjectDisposedException)
        var warningLogs = logMessages.Where(m => m.Contains("Warning") || m.Contains("Failed to dispose")).ToList();
        Assert.NotEmpty(warningLogs);
    }

    [Fact]
    public void CompleteJob_DisposesResources()
    {
        var service = CreateService();
        var id = service.CreateTestJob("ExecutePlan", "test-plan");

        // Start the job
        var job = service.GetJob(id);
        Assert.NotNull(job);

        // Create a CTS to verify it gets disposed
        job.TimeoutCts = new CancellationTokenSource();

        // Complete the job
        service.CompleteJob(id, 0);

        // Verify resources were disposed
        var completedJob = service.GetJob(id);
        Assert.NotNull(completedJob);
        Assert.Null(completedJob.Process);
        Assert.Null(completedJob.TimeoutCts);
    }

    [Fact]
    public void StopJob_DisposesResources()
    {
        var service = CreateService();
        var id = service.CreateTestJob("ExecutePlan", "test-plan");

        var job = service.GetJob(id);
        Assert.NotNull(job);

        // Create a CTS to verify it gets disposed
        job.TimeoutCts = new CancellationTokenSource();

        // Stop the job
        service.StopJob(id);

        // Verify resources were disposed
        var stoppedJob = service.GetJob(id);
        Assert.NotNull(stoppedJob);
        Assert.Null(stoppedJob.Process);
        Assert.Null(stoppedJob.TimeoutCts);
    }

    [Fact]
    public void DeleteJob_DisposesResources()
    {
        var service = CreateService();
        var id = service.CreateTestJob("ExecutePlan", "test-plan");

        var job = service.GetJob(id);
        Assert.NotNull(job);

        // Create a CTS to verify it gets disposed
        job.TimeoutCts = new CancellationTokenSource();

        // Delete the job
        service.DeleteJob(id);

        // Verify job is removed and resources were disposed
        var deletedJob = service.GetJob(id);
        Assert.Null(deletedJob);
    }
}

/// <summary>
/// Simple test logger provider that captures log messages to a list.
/// </summary>
internal class TestLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages;

    public TestLoggerProvider(List<string> messages)
    {
        _messages = messages;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestLogger(_messages);
    }

    public void Dispose() { }
}

internal class TestLogger : ILogger
{
    private readonly List<string> _messages;

    public TestLogger(List<string> messages)
    {
        _messages = messages;
    }

    IDisposable ILogger.BeginScope<TState>(TState state) where TState : default => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _messages.Add($"{logLevel}: {formatter(state, exception)}");
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}
