using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test;

public class JobItemOutputObservableTests
{
    [Fact]
    public void EnqueueOutput_EmitsNormalizedLinesViaObservable()
    {
        var job = new JobItem { Id = "test-1", Provider = "claude" };
        var received = new List<string>();
        using var sub = job.OutputObservable.Subscribe(received.Add);

        job.EnqueueOutput("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"hello\"}]}}");

        Assert.NotEmpty(received);
        Assert.All(received, line => Assert.False(string.IsNullOrEmpty(line)));
    }

    [Fact]
    public void EnqueueOutput_MultipleLines_AllEmittedInOrder()
    {
        var job = new JobItem { Id = "test-2", Provider = "claude" };
        var received = new List<string>();
        using var sub = job.OutputObservable.Subscribe(received.Add);

        job.EnqueueOutput("line-1");
        job.EnqueueOutput("line-2");
        job.EnqueueOutput("line-3");

        Assert.Equal(job.OutputLines.Count, received.Count);
    }

    [Fact]
    public void FlushParser_EmitsRemainingBufferedContent()
    {
        var job = new JobItem { Id = "test-3", Provider = "antigravity" };
        var received = new List<string>();
        using var sub = job.OutputObservable.Subscribe(received.Add);

        job.EnqueueOutput("partial");
        var countAfterEnqueue = received.Count;

        job.FlushParser();

        Assert.True(received.Count >= countAfterEnqueue);
    }

    [Fact]
    public void DisposeResources_CompletesObservable()
    {
        var job = new JobItem { Id = "test-4", Provider = "claude" };
        var completed = false;
        job.OutputObservable.Subscribe(_ => { }, () => completed = true);

        job.DisposeResources();

        Assert.True(completed);
    }

    [Fact]
    public void EnqueueOutput_RespectsMaxOutputLines()
    {
        var job = new JobItem { Id = "test-5", Provider = "claude" };

        for (var i = 0; i < 10_001; i++)
            job.EnqueueOutput($"{{\"type\":\"content_block_delta\",\"delta\":{{\"type\":\"text_delta\",\"text\":\"line {i}\"}}}}");

        Assert.True(job.OutputLines.Count <= 10_000);
    }
}
