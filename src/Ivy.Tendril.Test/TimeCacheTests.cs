using Ivy.Tendril.Helpers;
using Ivy.Tendril.Test.Helpers;

namespace Ivy.Tendril.Test;

public class TimeCacheTests
{
    [Fact]
    public void GetOrCompute_CallsComputeOnFirstAccess()
    {
        var cache = new TimeCache<int>(TimeSpan.FromMinutes(1));
        var callCount = 0;

        var result = cache.GetOrCompute(() =>
        {
            callCount++;
            return 42;
        });

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetOrCompute_ReturnsCachedValueWithinExpiration()
    {
        var cache = new TimeCache<int>(TimeSpan.FromSeconds(1));
        var callCount = 0;

        cache.GetOrCompute(() =>
        {
            callCount++;
            return 42;
        });
        var result = cache.GetOrCompute(() =>
        {
            callCount++;
            return 99;
        });

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrCompute_RecomputesAfterExpiration()
    {
        var cache = new TimeCache<int>(TimeSpan.FromMilliseconds(100));
        var callCount = 0;

        cache.GetOrCompute(() =>
        {
            callCount++;
            return 42;
        });

        // Wait for cache to expire by polling IsValid
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                await Task.Yield();
                return !cache.IsValid;
            },
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(50),
            "Cache did not expire within timeout");

        var result = cache.GetOrCompute(() =>
        {
            callCount++;
            return 99;
        });

        Assert.Equal(99, result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void Invalidate_ForcesCacheRecompute()
    {
        var cache = new TimeCache<int>(TimeSpan.FromMinutes(1));
        var callCount = 0;

        cache.GetOrCompute(() =>
        {
            callCount++;
            return 42;
        });
        cache.Invalidate();
        var result = cache.GetOrCompute(() =>
        {
            callCount++;
            return 99;
        });

        Assert.Equal(99, result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void IsValid_ReturnsTrueForValidCache()
    {
        var cache = new TimeCache<int>(TimeSpan.FromMinutes(1));
        cache.GetOrCompute(() => 42);

        Assert.True(cache.IsValid);
    }

    [Fact]
    public async Task IsValid_ReturnsFalseForExpiredCache()
    {
        var cache = new TimeCache<int>(TimeSpan.FromMilliseconds(50));
        cache.GetOrCompute(() => 42);

        // Wait for cache to expire
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                await Task.Yield();
                return !cache.IsValid;
            },
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(25),
            "Cache did not expire within timeout");

        Assert.False(cache.IsValid);
    }

    [Fact]
    public async Task GetOrComputeAsync_CallsComputeOnFirstAccess()
    {
        var cache = new TimeCache<int>(TimeSpan.FromMinutes(1));
        var callCount = 0;

        var result = await cache.GetOrComputeAsync(async () =>
        {
            await Task.Delay(10);
            callCount++;
            return 42;
        });

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrComputeAsync_ReturnsCachedValueWithinExpiration()
    {
        var cache = new TimeCache<int>(TimeSpan.FromSeconds(1));
        var callCount = 0;

        await cache.GetOrComputeAsync(async () =>
        {
            await Task.Delay(10);
            callCount++;
            return 42;
        });

        var result = await cache.GetOrComputeAsync(async () =>
        {
            await Task.Delay(10);
            callCount++;
            return 99;
        });

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrComputeAsync_RecomputesAfterExpiration()
    {
        var cache = new TimeCache<int>(TimeSpan.FromMilliseconds(100));
        var callCount = 0;

        await cache.GetOrComputeAsync(async () =>
        {
            await Task.Delay(10);
            callCount++;
            return 42;
        });

        // Wait for cache to expire by polling IsValid
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                await Task.Yield();
                return !cache.IsValid;
            },
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(50),
            "Cache did not expire within timeout");

        var result = await cache.GetOrComputeAsync(async () =>
        {
            await Task.Delay(10);
            callCount++;
            return 99;
        });

        Assert.Equal(99, result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetOrComputeAsync_PropagatesExceptions()
    {
        var cache = new TimeCache<int>(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await cache.GetOrComputeAsync(async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("Test exception");
            }));

        Assert.False(cache.IsValid);
    }

    [Fact]
    public async Task GetOrComputeAsync_WorksWithGetOrCompute()
    {
        var cache = new TimeCache<int>(TimeSpan.FromMinutes(1));

        await cache.GetOrComputeAsync(async () =>
        {
            await Task.Delay(10);
            return 42;
        });

        var result = cache.GetOrCompute(() => 99);

        Assert.Equal(42, result);
    }
}