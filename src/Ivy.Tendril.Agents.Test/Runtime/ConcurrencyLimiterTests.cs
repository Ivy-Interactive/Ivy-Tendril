using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class ConcurrencyLimiterTests : IDisposable
{
    private readonly ConcurrencyLimiter _limiter;

    public ConcurrencyLimiterTests()
    {
        _limiter = new ConcurrencyLimiter(new ConcurrencyOptions { MaxConcurrency = 2 });
    }

    public void Dispose() => _limiter.Dispose();

    [Fact]
    public async Task AcquireAsync_FirstSlot_ReturnsImmediately()
    {
        using var lease = await _limiter.AcquireAsync();
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task AcquireAsync_ExhaustsSlots_ReducesAvailable()
    {
        Assert.Equal(2, _limiter.AvailableSlots);

        using var lease1 = await _limiter.AcquireAsync();
        Assert.Equal(1, _limiter.AvailableSlots);

        using var lease2 = await _limiter.AcquireAsync();
        Assert.Equal(0, _limiter.AvailableSlots);
    }

    [Fact]
    public async Task AcquireAsync_Released_RestoresSlot()
    {
        var lease = await _limiter.AcquireAsync();
        Assert.Equal(1, _limiter.AvailableSlots);

        lease.Dispose();
        Assert.Equal(2, _limiter.AvailableSlots);
    }

    [Fact]
    public async Task AcquireAsync_DoubleDispose_SafeNoOp()
    {
        var lease = await _limiter.AcquireAsync();
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(2, _limiter.AvailableSlots);
    }

    [Fact]
    public async Task AcquireAsync_Cancelled_ThrowsOperationCanceled()
    {
        using var lease1 = await _limiter.AcquireAsync();
        using var lease2 = await _limiter.AcquireAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _limiter.AcquireAsync(cts.Token));
    }

    [Fact]
    public async Task AcquireAsync_WithQueueTimeout_ThrowsTimeoutException()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyOptions
        {
            MaxConcurrency = 1,
            QueueTimeout = TimeSpan.FromMilliseconds(50),
        });

        using var lease = await limiter.AcquireAsync();

        await Assert.ThrowsAsync<TimeoutException>(
            () => limiter.AcquireAsync());
    }

    [Fact]
    public async Task AcquireAsync_BlockedThenReleased_Unblocks()
    {
        using var lease1 = await _limiter.AcquireAsync();
        using var lease2 = await _limiter.AcquireAsync();

        var acquireTask = Task.Run(async () =>
        {
            using var lease3 = await _limiter.AcquireAsync();
            return true;
        });

        await Task.Delay(20);
        Assert.False(acquireTask.IsCompleted);

        lease1.Dispose();
        var result = await acquireTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(result);
    }

    [Fact]
    public void TryAcquire_SlotAvailable_ReturnsTrue()
    {
        Assert.True(_limiter.TryAcquire());
    }

    [Fact]
    public void TryAcquire_NoSlots_ReturnsFalse()
    {
        _limiter.TryAcquire();
        _limiter.TryAcquire();
        Assert.False(_limiter.TryAcquire());
    }

    [Fact]
    public void AvailableSlots_ReflectsMaxConcurrency()
    {
        Assert.Equal(2, _limiter.AvailableSlots);
    }
}
