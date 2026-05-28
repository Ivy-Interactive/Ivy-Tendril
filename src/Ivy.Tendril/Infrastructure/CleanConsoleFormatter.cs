using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Ivy.Tendril.Infrastructure;

public sealed class CleanConsoleFormatter : ConsoleFormatter
{
    public CleanConsoleFormatter() : base("clean") { }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null) return;
        textWriter.WriteLine(message);
    }
}
