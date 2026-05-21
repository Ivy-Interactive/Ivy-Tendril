namespace Ivy.Tendril.Agents.Abstractions;

public static class SessionRecording
{
    public static IDisposable RecordTo(this IAgentSession session, string logBasePath)
    {
        var path = Path.Combine(logBasePath, $"{session.SessionId}.jsonl");
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var writer = new StreamWriter(path, append: true) { AutoFlush = true };

        var sub = session.RawOutput?.Subscribe(
            line => writer.WriteLine(line),
            _ => writer.Dispose(),
            () => writer.Dispose()
        );

        if (sub is null)
        {
            writer.Dispose();
            return System.Reactive.Disposables.Disposable.Empty;
        }

        return System.Reactive.Disposables.Disposable.Create(() =>
        {
            sub.Dispose();
            writer.Dispose();
        });
    }

    public static async IAsyncEnumerable<string> ReadRawLines(
        string recordingPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(recordingPath);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) yield break;
            yield return line;
        }
    }

    public static async IAsyncEnumerable<AgentEvent> ReadEvents(
        string recordingPath,
        IEventParser parser,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var line in ReadRawLines(recordingPath, ct))
        {
            var events = parser.ParseLine(line);
            foreach (var evt in events)
                yield return evt;
        }
    }
}

public sealed record SessionRecordingInfo
{
    public required string SessionId { get; init; }
    public required string AgentId { get; init; }
    public required string Location { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public SessionState? FinalState { get; init; }
    public long? TotalLines { get; init; }
    public long? FileSizeBytes { get; init; }
    public SessionMetadata? Metadata { get; init; }
}
