using System.Buffers;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Copilot;

public sealed class CopilotEventParser : IEventParser
{
    public string AgentId => Abstractions.AgentId.Copilot;

    private static readonly IReadOnlyList<AgentEvent> Empty = Array.Empty<AgentEvent>();

    private static readonly HashSet<string> SkippedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "session.mcp_server_status_changed",
        "session.mcp_servers_loaded",
        "session.skills_loaded",
        "user.message",
        "assistant.turn_start",
        "assistant.turn_end",
        "assistant.message_start",
        "assistant.message_delta",
        "assistant.reasoning",
    };

    public IReadOnlyList<AgentEvent> ParseLine(string rawLine)
    {
        if (rawLine.Length == 0) return Empty;
        if (rawLine[0] != '{') return Empty;

        // Fast ephemeral check before full parse
        var span = rawLine.AsSpan();
        if (span.Contains("\"ephemeral\":true", StringComparison.Ordinal) ||
            span.Contains("\"ephemeral\": true", StringComparison.Ordinal))
            return Empty;

        var byteCount = Encoding.UTF8.GetByteCount(rawLine);
        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(rawLine, buffer);
            var reader = new Utf8JsonReader(buffer.AsSpan(0, written));

            var type = PeekType(ref reader);
            if (type is null)
                return [new UnknownEvent { Kind = AgentEventKind.Unknown, Content = rawLine, RawLine = rawLine }];

            if (SkippedTypes.Contains(type))
                return Empty;

            using var doc = JsonDocument.Parse(buffer.AsMemory(0, written));
            var root = doc.RootElement;

            return type switch
            {
                "session.tools_updated" => ParseToolsUpdated(root, rawLine),
                "assistant.message" => ParseAssistantMessage(root, rawLine),
                "tool.execution_complete" => ParseToolResult(root, rawLine),
                "result" => ParseResult(root, rawLine),
                _ => Empty
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

    private static IReadOnlyList<AgentEvent> ParseToolsUpdated(JsonElement root, string rawLine)
    {
        string? model = null;
        var tools = new List<string>();

        if (root.TryGetProperty("data", out var data))
        {
            model = data.TryGetProperty("model", out var m) ? m.GetString() : null;

            if (data.TryGetProperty("tools", out var toolsArr) && toolsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in toolsArr.EnumerateArray())
                {
                    var name = t.GetString();
                    if (name is not null) tools.Add(name);
                }
            }
        }

        return [new SessionInitEvent
        {
            Kind = AgentEventKind.SessionInit,
            SessionId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
            Model = model,
            AvailableTools = tools.Count > 0 ? tools : null,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseAssistantMessage(JsonElement root, string rawLine)
    {
        if (!root.TryGetProperty("data", out var data)) return Empty;

        var events = new List<AgentEvent>();

        // Extract text content
        if (data.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
        {
            var text = contentProp.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                events.Add(new TextEvent
                {
                    Kind = AgentEventKind.Text,
                    Text = text,
                    RawLine = rawLine,
                });
            }
        }

        // Extract tool requests
        if (data.TryGetProperty("toolRequests", out var toolRequests) && toolRequests.ValueKind == JsonValueKind.Array)
        {
            foreach (var req in toolRequests.EnumerateArray())
            {
                var toolCallId = req.TryGetProperty("toolCallId", out var tcId) ? tcId.GetString() ?? "" : "";
                var toolName = req.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" :
                               req.TryGetProperty("tool", out var tn2) ? tn2.GetString() ?? "" : "";
                var parameters = req.TryGetProperty("arguments", out var args) ? args.GetRawText() :
                                 req.TryGetProperty("parameters", out var param) ? param.GetRawText() : null;

                // Skip meta-tools
                if (string.Equals(toolName, "report_intent", StringComparison.OrdinalIgnoreCase))
                    continue;

                events.Add(new ToolCallEvent
                {
                    Kind = AgentEventKind.ToolCall,
                    ToolUseId = toolCallId,
                    ToolName = toolName,
                    InputJson = parameters,
                    RawLine = rawLine,
                });
            }
        }

        return events.Count > 0 ? events : Empty;
    }

    private static IReadOnlyList<AgentEvent> ParseToolResult(JsonElement root, string rawLine)
    {
        if (!root.TryGetProperty("data", out var data)) return Empty;

        var toolCallId = data.TryGetProperty("toolCallId", out var tcId) ? tcId.GetString() ?? "" : "";
        string? output = null;

        if (data.TryGetProperty("result", out var result))
        {
            if (result.TryGetProperty("content", out var content))
            {
                output = ContentExtractor.ExtractText(content);
            }
        }

        return [new ToolResultEvent
        {
            Kind = AgentEventKind.ToolResult,
            ToolUseId = toolCallId,
            Output = output,
            IsError = false,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseResult(JsonElement root, string rawLine)
    {
        var exitCode = root.TryGetProperty("exitCode", out var ec) ? ec.GetInt32() : 0;
        var sessionId = root.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;

        TimeSpan? duration = null;
        int? premiumRequests = null;
        int outputTokens = 0;

        if (root.TryGetProperty("usage", out var usage))
        {
            premiumRequests = usage.TryGetProperty("premiumRequests", out var pr) ? pr.GetInt32() : null;

            if (usage.TryGetProperty("sessionDurationMs", out var dur))
                duration = TimeSpan.FromMilliseconds(dur.GetInt64());
        }

        var agentUsage = new AgentUsage
        {
            OutputTokens = outputTokens,
            PremiumRequests = premiumRequests,
        };

        return [new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = exitCode == 0,
            ExitCode = exitCode,
            Duration = duration,
            Usage = agentUsage,
            RawLine = rawLine,
        }];
    }
}
