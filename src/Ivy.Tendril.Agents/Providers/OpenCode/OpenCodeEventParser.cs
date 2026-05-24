using System.Buffers;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

public sealed class OpenCodeEventParser : IEventParser
{
    public string AgentId => Abstractions.AgentId.OpenCode;

    private static readonly IReadOnlyList<AgentEvent> Empty = Array.Empty<AgentEvent>();

    private bool _hasError;

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

            using var doc = JsonDocument.Parse(buffer.AsMemory(0, written));
            var root = doc.RootElement;

            return type switch
            {
                "step_start" => ParseStepStart(root, rawLine),
                "text" => ParseText(root, rawLine),
                "tool_use" => ParseToolUse(root, rawLine),
                "step_finish" => ParseStepFinish(root, rawLine),
                "error" => ParseError(root, rawLine),
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
            {
                // Override success if an error event was seen (exit code is always 0)
                if (_hasError && result.IsSuccess)
                    return result with { ExitCode = exitCode, IsSuccess = false };
                return result with { ExitCode = exitCode };
            }
        }

        return new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = !_hasError && exitCode == 0,
            ExitCode = exitCode,
        };
    }

    public void Reset()
    {
        _hasError = false;
    }

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

    private static IReadOnlyList<AgentEvent> ParseStepStart(JsonElement root, string rawLine)
    {
        var sessionId = root.TryGetProperty("sessionID", out var sid) ? sid.GetString() ?? "" : "";

        return [new SessionInitEvent
        {
            Kind = AgentEventKind.SessionInit,
            SessionId = sessionId,
            Model = null,
            AvailableTools = null,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseText(JsonElement root, string rawLine)
    {
        if (!root.TryGetProperty("part", out var part)) return Empty;
        var text = part.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(text)) return Empty;

        return [new TextEvent
        {
            Kind = AgentEventKind.Text,
            Text = text,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseToolUse(JsonElement root, string rawLine)
    {
        if (!root.TryGetProperty("part", out var part)) return Empty;

        var toolName = part.TryGetProperty("tool", out var toolProp) ? toolProp.GetString() ?? "" : "";
        var callId = part.TryGetProperty("callID", out var cidProp) ? cidProp.GetString() ?? "" : "";

        string? inputJson = null;
        if (part.TryGetProperty("state", out var state) && state.TryGetProperty("input", out var input))
        {
            inputJson = input.GetRawText();
        }

        var events = new List<AgentEvent>
        {
            new ToolCallEvent
            {
                Kind = AgentEventKind.ToolCall,
                ToolUseId = callId,
                ToolName = toolName,
                InputJson = inputJson,
                RawLine = rawLine,
            }
        };

        // If tool has completed, also emit a result event
        if (part.TryGetProperty("state", out var stateEl) &&
            stateEl.TryGetProperty("status", out var statusProp) &&
            statusProp.GetString() == "completed")
        {
            var output = stateEl.TryGetProperty("output", out var outProp) ? ContentExtractor.ExtractText(outProp) : null;
            events.Add(new ToolResultEvent
            {
                Kind = AgentEventKind.ToolResult,
                ToolUseId = callId,
                ToolName = toolName,
                Output = output,
                IsError = false,
                RawLine = rawLine,
            });
        }

        return events;
    }

    private static IReadOnlyList<AgentEvent> ParseStepFinish(JsonElement root, string rawLine)
    {
        if (!root.TryGetProperty("part", out var part)) return Empty;

        var reason = part.TryGetProperty("reason", out var rProp) ? rProp.GetString() : null;

        // Intermediate steps (reason == "tool-calls") are not terminal — suppress them
        // to avoid the UI rendering each step as an error.
        if (reason is not ("stop" or "error"))
            return Empty;

        var cost = part.TryGetProperty("cost", out var cProp) ? cProp.GetDecimal() : (decimal?)null;

        int inputTokens = 0;
        int outputTokens = 0;
        if (part.TryGetProperty("tokens", out var tokens))
        {
            inputTokens = tokens.TryGetProperty("input", out var it) ? it.GetInt32() : 0;
            outputTokens = tokens.TryGetProperty("output", out var ot) ? ot.GetInt32() : 0;
        }

        AgentUsage? usage = (inputTokens > 0 || outputTokens > 0 || cost > 0)
            ? new AgentUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostUsd = cost,
            }
            : null;

        return [new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = reason == "stop",
            Usage = usage,
            RawLine = rawLine,
        }];
    }

    private IReadOnlyList<AgentEvent> ParseError(JsonElement root, string rawLine)
    {
        _hasError = true;

        var message = "";
        var isRetryable = false;
        var isAuthError = false;

        if (root.TryGetProperty("error", out var errorEl))
        {
            if (errorEl.TryGetProperty("data", out var data))
            {
                message = data.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
                isRetryable = data.TryGetProperty("isRetryable", out var retryProp) && retryProp.GetBoolean();

                if (data.TryGetProperty("statusCode", out var codeProp))
                {
                    var statusCode = codeProp.GetInt32();
                    isAuthError = statusCode is 401 or 403;
                }
            }
            else
            {
                message = errorEl.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
            }
        }

        return [new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = message,
            IsRetryable = isRetryable,
            IsAuthError = isAuthError,
            RawLine = rawLine,
        }];
    }
}
