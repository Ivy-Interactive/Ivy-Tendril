using System.Text;

namespace Ivy.Tendril.Helpers;

public static class ConsoleHelper
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    internal static string ReadStream(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static string ReadStdinToEnd()
    {
        // If Console.In is a StringReader (or SyncTextReader wrapping a StringReader/in-memory reader),
        // read from Console.In directly so test harness inputs work without blocking on OpenStandardInput.
        try
        {
            var inReader = Console.In;
            var inType = inReader.GetType();
            var field = inType.GetField("_in", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var inner = field?.GetValue(inReader) ?? inReader;
            if (inner is StringReader || inner.GetType().Name.Contains("StringReader"))
            {
                return FileHelper.SanitizeUtf8(inReader.ReadToEnd());
            }
        }
        catch
        {
            // Fall through to standard input handling
        }

        // Console.In decodes a redirected handle with the console codepage (measured: 437), which
        // mojibakes UTF-8 input: E2 80 94 (U+2014) arrives as three CP437 characters. Program.cs
        // cannot set Console.InputEncoding on a redirected handle, so decode the stream directly.
        if (!Console.IsInputRedirected)
            return FileHelper.SanitizeUtf8(Console.In.ReadToEnd());

        var stream = Console.OpenStandardInput();
        return FileHelper.SanitizeUtf8(ReadStream(stream));
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
        !string.IsNullOrEmpty(filePath) ? FileHelper.ReadAllText(filePath) : fallback();

    // Resolves long-form command input from exactly one explicit source.
    // STDIN is read ONLY when stdin is true (never as an implicit fallback).
    public static string ResolveInput(bool stdin, string? filePath, string? inlineValue) =>
        stdin ? ReadStdinWithTimeout()
        : !string.IsNullOrEmpty(filePath) ? FileHelper.ReadAllText(filePath)
        : inlineValue ?? "";
}
