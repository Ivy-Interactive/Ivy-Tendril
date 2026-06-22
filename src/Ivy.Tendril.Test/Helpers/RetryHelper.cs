namespace Ivy.Tendril.Test.Helpers;

public static class RetryHelper
{
    /// <summary>
    ///     Synchronously polls <paramref name="condition" /> until it returns true or the timeout
    ///     elapses. Use this instead of a fixed <c>Thread.Sleep</c> before an assertion on async /
    ///     background / timer / file-watcher work — it removes load-sensitive flakiness by waiting
    ///     for the observable result rather than guessing how long the work takes.
    /// </summary>
    /// <returns>True if the condition became true within the timeout; false otherwise.</returns>
    public static bool WaitUntil(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(25);
        var deadline = Environment.TickCount64 + (long)limit.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(interval);
        }

        return condition();
    }

    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        string? failureMessage = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(100);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(interval);
        }

        throw new TimeoutException(
            failureMessage ?? $"Condition not met within {timeout.TotalSeconds}s");
    }

    public static async Task RetryAsync(
        Func<Task> action,
        int maxAttempts = 3,
        TimeSpan? delayBetween = null)
    {
        var delay = delayBetween ?? TimeSpan.FromMilliseconds(500);
        Exception? lastException = null;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (i < maxAttempts - 1)
                    await Task.Delay(delay);
            }
        }

        throw new AggregateException(
            $"Action failed after {maxAttempts} attempts", lastException!);
    }
}
