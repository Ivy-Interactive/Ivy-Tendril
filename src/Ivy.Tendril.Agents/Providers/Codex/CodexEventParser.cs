using System.Buffers;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Codex;

public sealed class CodexEventParser : IEventParser
{
    public string AgentId => Abstractions.AgentId.Codex;

    private static readonly IReadOnlyList<AgentEvent> Empty = Array.Empty<AgentEvent>();

    public IReadOnlyList<AgentEvent> ParseLine(string rawLine)
    {
        if (rawLine.Length == 0) return Empty;
        if (rawLine[0] != '{') return Empty;

        var byteCount = Encoding.UTF8.GetByteCount(rawLine);
        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(rawLine, buffer);
            var reader = new Utf8JsonReader(buffer.AsSpan(0, written));

            var type = PeekType(ref reader);
            if (type is null)
                return [new UnknownEvent { Kind = AgentEventKind.Unknown, Content = rawLine, RawLine = rawLine }];

            // Skip noise events that carry no actionable data
            if (type is "turn.started" or "item.started" or "item.updated")
                return Empty;

            using var doc = JsonDocument.Parse(buffer.AsMemory(0, written));
            var root = doc.RootElement;

            return type switch
            {
                "thread.started" => ParseThreadStarted(root, rawLine),
                "item.completed" => ParseItemCompleted(root, rawLine),
                "turn.completed" => ParseTurnCompleted(root, rawLine),
                _ => [new UnknownEvent { Kind = AgentEventKind.Unknown, Content = rawLine, RawLine = rawLine }]
            };
        }
        catch (JsonException)
        {
            return [new UnknownEvent { Kind = AgentEventKind.Unknown, Content = rawLine, RawLine = rawLine }];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public IReadOnlyList<AgentEvent> Flush() => Empty;

    public ResultEvent? BuildResult(IReadOnlyList<AgentEvent> events, int exitCode)
    {
        for (var i = events.Count - 1; i >= 0; i--)
        {
            if (events[i] is ResultEvent result)
                return result with { ExitCode = exitCode };
        }

        return new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = exitCode == 0,
            ExitCode = exitCode,
        };
    }

    public void Reset() { }

    private static string? PeekType(ref Utf8JsonReader reader)
    {
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("type"u8))
                {
                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                        return reader.GetString();
                    return null;
                }

                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    reader.Skip();
            }
        }
        catch (JsonException) { }

        return null;
    }

    private static IReadOnlyList<AgentEvent> ParseThreadStarted(JsonElement root, string rawLine)
    {
        var threadId = root.TryGetProperty("thread_id", out var tid) ? tid.GetString() ?? "" : "";

        return [new SessionInitEvent
        {
            Kind = AgentEventKind.SessionInit,
            SessionId = threadId,
            Model = null,
            AvailableTools = null,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseItemCompleted(JsonElement root, string rawLine)
    {
        if (!root.TryGetProperty("item", out var item))
            return [new UnknownEvent { Kind = AgentEventKind.Unknown, Content = rawLine, RawLine = rawLine }];

        var itemType = item.TryGetProperty("type", out var itProp) ? itProp.GetString() : null;
        var itemId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";

        return itemType switch
        {
            "agent_message" => ParseAgentMessage(item, itemId, rawLine),
            "command_execution" => ParseCommandExecution(item, itemId, rawLine),
            _ => [new UnknownEvent { Kind = AgentEventKind.Unknown, Content = rawLine, RawLine = rawLine }]
        };
    }

    private static IReadOnlyList<AgentEvent> ParseAgentMessage(JsonElement item, string itemId, string rawLine)
    {
        var text = item.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(text))
            return Empty;

        return [new TextEvent
        {
            Kind = AgentEventKind.Text,
            Text = text,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseCommandExecution(JsonElement item, string itemId, string rawLine)
    {
        var command = item.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() ?? "" : "";
        var output = item.TryGetProperty("aggregated_output", out var outProp) ? ContentExtractor.ExtractText(outProp) : null;

        return
        [
            new ToolCallEvent
            {
                Kind = AgentEventKind.ToolCall,
                ToolUseId = itemId,
                ToolName = "bash",
                InputJson = JsonSerializer.Serialize(new { command }),
                RawLine = rawLine,
            },
            new ToolResultEvent
            {
                Kind = AgentEventKind.ToolResult,
                ToolUseId = itemId,
                ToolName = "bash",
                Output = output,
                IsError = false,
                RawLine = rawLine,
            }
        ];
    }

    private static IReadOnlyList<AgentEvent> ParseTurnCompleted(JsonElement root, string rawLine)
    {
        AgentUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageEl))
        {
            usage = new AgentUsage
            {
                InputTokens = usageEl.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                OutputTokens = usageEl.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
                CacheReadTokens = usageEl.TryGetProperty("cached_input_tokens", out var cr) ? cr.GetInt32() : 0,
            };
        }

        return [new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = true,
            Usage = usage,
            RawLine = rawLine,
        }];
    }
}
