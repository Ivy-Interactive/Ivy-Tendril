namespace Ivy.Tendril.Helpers;

public static class ConsoleHelper
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static string ReadStdinWithTimeout(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var readTask = Task.Run(() => Console.In.ReadToEnd());
        if (!readTask.Wait(effectiveTimeout))
            throw new InvalidOperationException(
                $"No content received on STDIN within {effectiveTimeout.TotalSeconds}s. " +
                "Pipe content to this command or use a file-based alternative.");
        return readTask.Result;
    }
}
