using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class ConcurrencyLimiter(ConcurrencyOptions options) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(options.MaxConcurrency, options.MaxConcurrency);
    private readonly TimeSpan? _queueTimeout = options.QueueTimeout;

    public int AvailableSlots => _semaphore.CurrentCount;

    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        var acquired = _queueTimeout.HasValue
            ? await _semaphore.WaitAsync(_queueTimeout.Value, ct)
            : await WaitIndefinitelyAsync(ct);

        if (!acquired)
            throw new TimeoutException(
                $"Timed out waiting for a concurrency slot after {_queueTimeout!.Value.TotalSeconds:F0}s");

        return new Release(_semaphore);
    }

    public bool TryAcquire()
    {
        return _semaphore.Wait(TimeSpan.Zero);
    }

    private async Task<bool> WaitIndefinitelyAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        return true;
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _released, 1, 0) == 0)
                semaphore.Release();
        }
    }
}
