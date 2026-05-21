using System.Buffers;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Claude;

public sealed class ClaudeEventParser : IEventParser
{
    public string AgentId => Abstractions.AgentId.Claude;

    private static readonly IReadOnlyList<AgentEvent> Empty = Array.Empty<AgentEvent>();

    public IReadOnlyList<AgentEvent> ParseLine(string rawLine)
    {
        if (rawLine.Length == 0) return Empty;
        if (rawLine[0] != '{') return Empty;

        // Fast skip for known noise patterns using span comparison
        var span = rawLine.AsSpan();
        if (span.Contains("\"type\":\"heartbeat\"", StringComparison.Ordinal)) return Empty;

        var byteCount = Encoding.UTF8.GetByteCount(rawLine);
        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(rawLine, buffer);
            var reader = new Utf8JsonReader(buffer.AsSpan(0, written));

            // Peek at the type field using Utf8JsonReader for fast dispatch
            var type = PeekType(ref reader);
            if (type is null)
                return [new UnknownEvent { Kind = AgentEventKind.Unknown, Content = rawLine, RawLine = rawLine }];

            // For the actual parsing, use JsonDocument (needed for nested access)
            // The Utf8JsonReader peek avoids allocating a DOM for skippable types
            if (type is "heartbeat" or "hook_started" or "hook_response")
                return Empty;

            using var doc = JsonDocument.Parse(buffer.AsMemory(0, written));
            var root = doc.RootElement;

            return type switch
            {
                "system" => ParseSystem(root, rawLine),
                "assistant" => ParseAssistant(root, rawLine),
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

    private static IReadOnlyList<AgentEvent> ParseSystem(JsonElement root, string rawLine)
    {
        var subtype = root.TryGetProperty("subtype", out var st) ? st.GetString() : null;

        if (subtype == "init")
        {
            var sessionId = root.TryGetProperty("session_id", out var sid) ? sid.GetString() ?? "" : "";
            var model = root.TryGetProperty("model", out var m) ? m.GetString() : null;
            var tools = new List<string>();

            if (root.TryGetProperty("tools", out var toolsArr) && toolsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in toolsArr.EnumerateArray())
                {
                    var name = t.GetString();
                    if (name is not null) tools.Add(name);
                }
            }

            return [new SessionInitEvent
            {
                Kind = AgentEventKind.SessionInit,
                SessionId = sessionId,
                Model = model,
                AvailableTools = tools,
                RawLine = rawLine,
            }];
        }

        if (subtype is "hook_started" or "hook_response")
            return Empty;

        return [new SystemEvent
        {
            Kind = AgentEventKind.System,
            Subtype = subtype ?? "unknown",
            Message = rawLine,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseAssistant(JsonElement root, string rawLine)
    {
        if (!root.TryGetProperty("message", out var message)) return Empty;
        if (!message.TryGetProperty("content", out var content)) return Empty;
        if (content.ValueKind != JsonValueKind.Array) return Empty;

        var events = new List<AgentEvent>();

        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var blockType)) continue;
            var bt = blockType.GetString();

            switch (bt)
            {
                case "text":
                    var text = block.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(text))
                    {
                        events.Add(new TextEvent
                        {
                            Kind = AgentEventKind.Text,
                            Text = text,
                            RawLine = rawLine,
                        });
                    }
                    break;

                case "thinking":
                    var thinking = block.TryGetProperty("thinking", out var thinkProp) ? thinkProp.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(thinking))
                    {
                        events.Add(new ThinkingEvent
                        {
                            Kind = AgentEventKind.Thinking,
                            Content = thinking,
                            RawLine = rawLine,
                        });
                    }
                    break;

                case "tool_use":
                    var toolId = block.TryGetProperty("id", out var tidProp) ? tidProp.GetString() ?? "" : "";
                    var toolName = block.TryGetProperty("name", out var tnProp) ? tnProp.GetString() ?? "" : "";
                    var inputJson = block.TryGetProperty("input", out var inputProp)
                        ? inputProp.GetRawText()
                        : null;

                    events.Add(new ToolCallEvent
                    {
                        Kind = AgentEventKind.ToolCall,
                        ToolUseId = toolId,
                        ToolName = toolName,
                        InputJson = inputJson,
                        RawLine = rawLine,
                    });
                    break;

                case "tool_result":
                    var resultToolId = block.TryGetProperty("tool_use_id", out var rtidProp) ? rtidProp.GetString() ?? "" : "";
                    var output = block.TryGetProperty("content", out var outProp) ? outProp.GetString() : null;
                    var isError = block.TryGetProperty("is_error", out var errProp) && errProp.GetBoolean();

                    events.Add(new ToolResultEvent
                    {
                        Kind = AgentEventKind.ToolResult,
                        ToolUseId = resultToolId,
                        Output = output,
                        IsError = isError,
                        RawLine = rawLine,
                    });
                    break;
            }
        }

        return events.Count > 0 ? events : Empty;
    }

    private static IReadOnlyList<AgentEvent> ParseResult(JsonElement root, string rawLine)
    {
        var resultText = root.TryGetProperty("result", out var rp) ? rp.GetString() : null;
        var isError = root.TryGetProperty("is_error", out var ep) && ep.GetBoolean();
        var durationMs = root.TryGetProperty("duration_ms", out var dp) ? dp.GetInt64() : 0;
        var numTurns = root.TryGetProperty("num_turns", out var ntp) ? ntp.GetInt32() : (int?)null;
        var costUsd = root.TryGetProperty("total_cost_usd", out var cp) ? cp.GetDecimal() : (decimal?)null;

        AgentUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageEl))
        {
            usage = new AgentUsage
            {
                InputTokens = usageEl.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                OutputTokens = usageEl.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
                CacheReadTokens = usageEl.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : 0,
                CacheWriteTokens = usageEl.TryGetProperty("cache_creation_input_tokens", out var cc) ? cc.GetInt32() : 0,
                CostUsd = costUsd,
            };
        }

        var denials = new List<PermissionDenialEvent>();
        if (root.TryGetProperty("permission_denials", out var pdArr) && pdArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var denial in pdArr.EnumerateArray())
            {
                var toolName = denial.TryGetProperty("tool_name", out var tn) ? tn.GetString() : null;
                if (string.IsNullOrEmpty(toolName)) continue;

                string? inputSummary = null;
                if (denial.TryGetProperty("tool_input", out var input))
                {
                    if (input.TryGetProperty("file_path", out var fp))
                        inputSummary = fp.GetString();
                    else if (input.TryGetProperty("command", out var cmd))
                    {
                        var cmdStr = cmd.GetString() ?? "";
                        inputSummary = cmdStr.Length > 80 ? cmdStr[..80] + "..." : cmdStr;
                    }
                }

                denials.Add(new PermissionDenialEvent
                {
                    Kind = AgentEventKind.PermissionDenial,
                    ToolName = toolName,
                    InputSummary = inputSummary,
                });
            }
        }

        return [new ResultEvent
        {
            Kind = AgentEventKind.Result,
            Response = resultText,
            IsSuccess = !isError,
            Duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : null,
            TurnCount = numTurns,
            Usage = usage,
            PermissionDenials = denials,
            RawLine = rawLine,
        }];
    }
}
