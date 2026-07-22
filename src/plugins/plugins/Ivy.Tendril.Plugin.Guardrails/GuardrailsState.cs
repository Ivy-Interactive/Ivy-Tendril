using System.Collections.Concurrent;
using Ivy.Plugins.Hooks;

namespace Ivy.Tendril.Plugin.Guardrails;

/// <summary>
/// Tracks per-project job outcomes in memory to detect degraded projects.
/// </summary>
internal class GuardrailsState
{
    private readonly ConcurrentDictionary<string, Queue<JobStatus>> _history = new();
    private const int MaxHistory = 20;

    public void RecordJob(string project, JobStatus status)
    {
        var queue = _history.GetOrAdd(project, _ => new Queue<JobStatus>());
        lock (queue)
        {
            queue.Enqueue(status);
            while (queue.Count > MaxHistory)
                queue.Dequeue();
        }
    }

    public bool IsProjectDegraded(string project, int threshold)
    {
        if (!_history.TryGetValue(project, out var queue))
            return false;

        lock (queue)
        {
            if (queue.Count < threshold)
                return false;

            // Check if the last N jobs all failed
            return queue.Reverse().Take(threshold).All(s => s == JobStatus.Failed);
        }
    }

    public void Reset()
    {
        _history.Clear();
    }
}
