namespace Ivy.Tendril.Helpers;

/// <summary>
///     Cross-process file lock for config.yaml read-modify-write cycles.
///     CLI/MCP commands each load the whole file, mutate one field, and serialize the whole
///     graph back, so two unsynchronized writers silently drop one another's change.
///     <see cref="Services.ConfigService.MutateAndSave"/> is the single acquisition site: acquiring
///     again while the lock is held (for example from a nested WriteSettingsToDisk) would deadlock.
/// </summary>
public static class ConfigFileLock
{
    private const int MaxRetries = 50;
    private const int DelayMs = 100;

    /// <summary>
    ///     FileShare.None does not exclude two handles opened by the same process, and the TUI has
    ///     many in-process SaveSettings() sites plus a FileSystemWatcher reload, so the file lock
    ///     alone is not enough. Same belt-and-braces pairing as PlanYamlHelper.AllocatePlanId, but a
    ///     semaphore rather than a monitor because the handle may be disposed on another thread.
    /// </summary>
    private static readonly SemaphoreSlim InProcessLock = new(1, 1);

    /// <summary>
    ///     Blocks until exclusive access to <paramref name="configPath"/> is held, then returns a
    ///     handle that releases it on dispose. Throws <see cref="TimeoutException"/> after the retry
    ///     budget expires, which the CLI surfaces as "Error: ..." and exit 1: the loser of a race
    ///     fails loudly instead of silently dropping the winner's write.
    ///     An empty <paramref name="configPath"/> (the fresh-install path where TendrilHome is unset)
    ///     yields a no-op handle rather than throwing.
    /// </summary>
    public static IDisposable Acquire(string configPath) =>
        Acquire(configPath, MaxRetries, DelayMs);

    /// <summary>Overload with an explicit retry budget so tests can assert the timeout without waiting for it.</summary>
    internal static IDisposable Acquire(string configPath, int maxRetries, int delayMs)
    {
        if (string.IsNullOrEmpty(configPath))
            return new NoOpLock();

        var lockPath = configPath + ".lock";
        var budgetMs = maxRetries * delayMs;

        if (!InProcessLock.Wait(budgetMs))
            throw new TimeoutException(
                $"Could not acquire config lock at {lockPath} after {budgetMs}ms (held by this process)");

        try
        {
            for (var i = 0; i < maxRetries; i++)
                try
                {
                    var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                        FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
                    return new FileLockHandle(stream);
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    Thread.Sleep(delayMs);
                }
                catch (IOException ex)
                {
                    // Budget exhausted. Translate rather than letting the raw sharing violation out:
                    // "cannot access the file" does not say that we waited, and callers should see the
                    // same exception whichever holder (this process or another) won.
                    // PlanFileLock leaves its equivalent throw unreachable for this reason.
                    throw new TimeoutException(
                        $"Could not acquire config lock at {lockPath} after {budgetMs}ms", ex);
                }

            throw new TimeoutException($"Could not acquire config lock at {lockPath} after {budgetMs}ms");
        }
        catch
        {
            InProcessLock.Release();
            throw;
        }
    }

    /// <summary>Releases the file handle first, then the in-process semaphore, mirroring acquisition order.</summary>
    private sealed class FileLockHandle(FileStream stream) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            stream.Dispose();
            InProcessLock.Release();
        }
    }

    private sealed class NoOpLock : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
