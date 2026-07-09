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

    public static string? ResolveContent(string? filePath, Func<string?> fallback) =>
        !string.IsNullOrEmpty(filePath) ? File.ReadAllText(filePath) : fallback();

    // Resolves long-form command input from exactly one explicit source.
    // STDIN is read ONLY when stdin is true (never as an implicit fallback).
    public static string ResolveInput(bool stdin, string? filePath, string? inlineValue) =>
        stdin ? ReadStdinWithTimeout()
        : !string.IsNullOrEmpty(filePath) ? File.ReadAllText(filePath)
        : inlineValue ?? "";
}
