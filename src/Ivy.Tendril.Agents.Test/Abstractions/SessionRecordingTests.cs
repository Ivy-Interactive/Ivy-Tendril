using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class SessionRecordingTests
{
    [Fact]
    public async Task ReadRawLines_ReadsAllLines()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(tempFile, ["line1", "line2", "line3"]);

            var lines = new List<string>();
            await foreach (var line in SessionRecording.ReadRawLines(tempFile))
                lines.Add(line);

            Assert.Equal(3, lines.Count);
            Assert.Equal("line1", lines[0]);
            Assert.Equal("line3", lines[2]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadEvents_ParsesLinesViaParser()
    {
        var tempFile = Path.GetTempFileName();
        var parser = new ClaudeEventParser();
        try
        {
            var jsonLines = new[]
            {
                """{"type":"system","subtype":"init","session_id":"s1","model":"test","tools":[]}""",
                """{"type":"assistant","message":{"id":"m1","content":[{"type":"text","text":"hello"}]}}""",
                """{"type":"result","is_error":false,"duration_ms":100,"result":"hello"}""",
            };
            await File.WriteAllLinesAsync(tempFile, jsonLines);

            var events = new List<AgentEvent>();
            await foreach (var evt in SessionRecording.ReadEvents(tempFile, parser))
                events.Add(evt);

            Assert.Equal(3, events.Count);
            Assert.IsType<SessionInitEvent>(events[0]);
            Assert.IsType<TextEvent>(events[1]);
            Assert.IsType<ResultEvent>(events[2]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadRawLines_EmptyFile_ReturnsEmpty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var lines = new List<string>();
            await foreach (var line in SessionRecording.ReadRawLines(tempFile))
                lines.Add(line);

            Assert.Empty(lines);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadRawLines_Cancellation_StopsReading()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(tempFile, ["line1", "line2", "line3"]);

            using var cts = new CancellationTokenSource();
            var lines = new List<string>();
            await foreach (var line in SessionRecording.ReadRawLines(tempFile, cts.Token))
            {
                lines.Add(line);
                if (lines.Count == 1) cts.Cancel();
            }

            Assert.Single(lines);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SessionRecordingInfo_CreatesCorrectly()
    {
        var info = new SessionRecordingInfo
        {
            SessionId = "s-123",
            AgentId = AgentId.Claude,
            Location = "/logs/s-123.jsonl",
            CreatedAt = DateTimeOffset.UtcNow,
            FinalState = SessionState.Completed,
            TotalLines = 50,
            FileSizeBytes = 4096,
        };

        Assert.Equal("s-123", info.SessionId);
        Assert.Equal(SessionState.Completed, info.FinalState);
        Assert.Equal(50, info.TotalLines);
    }
}
