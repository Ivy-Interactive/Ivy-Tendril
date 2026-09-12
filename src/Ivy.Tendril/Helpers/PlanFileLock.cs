namespace Ivy.Tendril.Helpers;

/// <summary>
///     Cross-process file lock for plan.yaml writes.
///     Both PlanReaderService (in-process) and PlanCommandHelpers (CLI) use this
///     to coordinate concurrent read-modify-write operations on the same plan.yaml.
/// </summary>
public static class PlanFileLock
{
    private const int MaxRetries = 50;
    private const int DelayMs = 100;

    /// <summary>
    ///     The longest <see cref="Acquire" /> can take before it gives up. Callers that block on a write
    ///     which has to take this lock size their own timeout from it rather than guessing (#2571).
    /// </summary>
    public static TimeSpan AcquireBudget { get; } = TimeSpan.FromMilliseconds(MaxRetries * DelayMs);

    public static FileStream Acquire(string planFolder)
    {
        var lockPath = Path.Combine(planFolder, "plan.yaml.lock");

        for (var i = 0; i < MaxRetries; i++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (i < MaxRetries - 1)
            {
                Thread.Sleep(DelayMs);
            }
        }

        throw new TimeoutException($"Could not acquire plan lock at {lockPath} after {MaxRetries * DelayMs}ms");
    }
}
