namespace Ivy.Tendril.Test.End2End.Helpers;

public static class RetryHelper
{
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        string? failureMessage = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);
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
