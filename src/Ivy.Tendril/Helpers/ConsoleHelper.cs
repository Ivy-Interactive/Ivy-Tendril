using System.Text;

namespace Ivy.Tendril.Helpers;

public static class ConsoleHelper
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    internal static string ReadStream(Stream stream)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ReadStdinToEnd()
    {
        // Console.In decodes a redirected handle with the console codepage (measured: 437), which
        // mojibakes UTF-8 input: E2 80 94 (U+2014) arrives as three CP437 characters. Program.cs
        // cannot set Console.InputEncoding on a redirected handle, so decode the stream directly.
        if (!Console.IsInputRedirected)
            return Console.In.ReadToEnd();

        using var stream = Console.OpenStandardInput();
        return ReadStream(stream);
    }

    public static string ReadStdinWithTimeout(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var readTask = Task.Run(ReadStdinToEnd);
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
