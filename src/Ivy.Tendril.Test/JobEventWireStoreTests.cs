using System.Collections.Concurrent;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test;

public class JobEventWireStoreTests : IDisposable
{
    private readonly string _tendrilHome;

    public JobEventWireStoreTests()
    {
        _tendrilHome = Path.Combine(Path.GetTempPath(), $"tendril-eventwire-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tendrilHome);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tendrilHome))
                Directory.Delete(_tendrilHome, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void Write_ThenRead_RoundTripsLinesInOrder()
    {
        var job = new JobItem { Id = "job-1" };
        job.OutputLines.Enqueue("line-1");
        job.OutputLines.Enqueue("line-2");
        job.OutputLines.Enqueue("line-3");

        JobEventWireStore.Write(_tendrilHome, job);
        var result = JobEventWireStore.Read(_tendrilHome, "job-1");

        Assert.NotNull(result);
        Assert.Equal(new[] { "line-1", "line-2", "line-3" }, result!.ToArray());
    }

    [Fact]
    public void Write_WithEmptyOutputLines_DoesNotCreateFile()
    {
        var job = new JobItem { Id = "job-empty", OutputLines = new ConcurrentQueue<string>() };

        JobEventWireStore.Write(_tendrilHome, job);

        Assert.False(File.Exists(JobEventWireStore.GetFilePath(_tendrilHome, "job-empty")));
    }

    [Fact]
    public void Read_MissingFile_ReturnsNull()
    {
        var result = JobEventWireStore.Read(_tendrilHome, "nonexistent-job");

        Assert.Null(result);
    }

    [Fact]
    public void Delete_MissingFile_DoesNotThrow()
    {
        var exception = Record.Exception(() => JobEventWireStore.Delete(_tendrilHome, "nonexistent-job"));

        Assert.Null(exception);
    }
}
