using System.Buffers;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiEventParser : IEventParser
{
    public string AgentId => Abstractions.AgentId.Gemini;

    private static readonly IReadOnlyList<AgentEvent> Empty = Array.Empty<AgentEvent>();
    private const string StderrPrefix = "[stderr] ";

    public IReadOnlyList<AgentEvent> ParseLine(string rawLine)
    {
        if (rawLine.Length == 0) return Empty;

        if (rawLine.StartsWith(StderrPrefix, StringComparison.Ordinal))
            return Empty;

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
                "init" => ParseInit(root, rawLine),
                "message" => ParseMessage(root, rawLine),
                "tool_use" => ParseToolUse(root, rawLine),
                "tool_result" => ParseToolResult(root, rawLine),
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

    private static IReadOnlyList<AgentEvent> ParseInit(JsonElement root, string rawLine)
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

    private static IReadOnlyList<AgentEvent> ParseMessage(JsonElement root, string rawLine)
    {
        // Skip user message echoes — only emit assistant messages as text
        var role = root.TryGetProperty("role", out var rp) ? rp.GetString() : null;
        if (role == "user")
            return Empty;

        var content = root.TryGetProperty("content", out var cp) ? cp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(content))
            return Empty;

        var unwrapped = UnwrapHardWraps(content);

        return [new TextEvent
        {
            Kind = AgentEventKind.Text,
            Text = unwrapped,
            RawLine = rawLine,
        }];
    }

    private static string UnwrapHardWraps(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var lines = content.Split('\n');
        if (lines.Length <= 1)
            return content;

        var result = new StringBuilder();
        var inCodeBlock = false;
        var previousWasProse = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedLine = line.TrimStart();

            // Track code block boundaries
            if (trimmedLine.StartsWith("```", StringComparison.Ordinal) ||
                trimmedLine.StartsWith("~~~", StringComparison.Ordinal))
            {
                if (result.Length > 0 && !inCodeBlock)
                    result.Append('\n');
                result.Append(line);
                result.Append('\n');
                inCodeBlock = !inCodeBlock;
                previousWasProse = false;
                continue;
            }

            // Preserve code block content exactly
            if (inCodeBlock)
            {
                result.Append(line);
                result.Append('\n');
                previousWasProse = false;
                continue;
            }

            // Track blank lines (paragraph boundaries)
            var isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank)
            {
                // Preserve paragraph breaks
                result.Append('\n');
                result.Append('\n');
                previousWasProse = false;
                continue;
            }

            // Check if this line is structural
            var isHeading = trimmedLine.StartsWith('#');
            var isListItem = trimmedLine.StartsWith("- ", StringComparison.Ordinal) ||
                             trimmedLine.StartsWith("* ", StringComparison.Ordinal) ||
                             trimmedLine.StartsWith("+ ", StringComparison.Ordinal) ||
                             (trimmedLine.Length > 0 && char.IsDigit(trimmedLine[0]) &&
                              trimmedLine.Contains(". ", StringComparison.Ordinal));

            var isStructural = isHeading || isListItem;
            var isProse = !isStructural;

            // First line
            if (result.Length == 0)
            {
                result.Append(line);
                previousWasProse = isProse;
                continue;
            }

            // Only join if both previous and current are prose
            if (previousWasProse && isProse)
            {
                result.Append(' ');
                result.Append(line);
                previousWasProse = true;
            }
            else
            {
                // Only add newline if the result doesn't already end with one
                if (result.Length > 0 && result[result.Length - 1] != '\n')
                    result.Append('\n');
                result.Append(line);
                previousWasProse = isProse;
            }
        }

        return result.ToString();
    }

    private static IReadOnlyList<AgentEvent> ParseToolUse(JsonElement root, string rawLine)
    {
        // Gemini format: tool_id, tool_name, parameters
        var toolId = root.TryGetProperty("tool_id", out var tidProp) ? tidProp.GetString() ?? "" : "";
        var toolName = root.TryGetProperty("tool_name", out var tnProp) ? tnProp.GetString() ?? "" : "";
        var inputJson = root.TryGetProperty("parameters", out var paramsProp) ? paramsProp.GetRawText() : null;

        // Convert update_topic to a TextEvent showing the topic title/summary
        if (toolName == "update_topic" && paramsProp.ValueKind == JsonValueKind.Object)
        {
            var title = paramsProp.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
            var summary = paramsProp.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() : null;
            var text = !string.IsNullOrEmpty(summary) ? summary : title;
            if (!string.IsNullOrEmpty(text))
                return [new TextEvent { Kind = AgentEventKind.Text, Text = text, RawLine = rawLine }];
            return Empty;
        }

        string? description = null;
        if (paramsProp.ValueKind == JsonValueKind.Object &&
            paramsProp.TryGetProperty("description", out var descProp) &&
            descProp.ValueKind == JsonValueKind.String)
            description = descProp.GetString();

        return [new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = toolId,
            ToolName = toolName,
            InputJson = inputJson,
            Description = description,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseToolResult(JsonElement root, string rawLine)
    {
        // Gemini format: tool_id, status, output
        var toolId = root.TryGetProperty("tool_id", out var tidProp) ? tidProp.GetString() ?? "" : "";

        // Suppress results for update_topic (converted to TextEvent in ParseToolUse)
        if (toolId.StartsWith("update_topic", StringComparison.Ordinal))
            return Empty;

        var output = root.TryGetProperty("output", out var outProp) ? outProp.GetString() : null;
        var status = root.TryGetProperty("status", out var stProp) ? stProp.GetString() : null;
        var isError = status != null && !status.Equals("success", StringComparison.OrdinalIgnoreCase);

        return [new ToolResultEvent
        {
            Kind = AgentEventKind.ToolResult,
            ToolUseId = toolId,
            Output = output,
            IsError = isError,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseResult(JsonElement root, string rawLine)
    {
        var response = root.TryGetProperty("response", out var rp) ? rp.GetString() : null;

        AgentUsage? usage = null;
        if (root.TryGetProperty("stats", out var stats) &&
            stats.TryGetProperty("models", out var models) &&
            models.ValueKind == JsonValueKind.Array)
        {
            long totalInput = 0;
            long totalOutput = 0;
            long totalCacheRead = 0;

            foreach (var entry in models.EnumerateArray())
            {
                if (entry.TryGetProperty("prompt", out var prompt))
                    totalInput += prompt.GetInt64();
                if (entry.TryGetProperty("candidates", out var candidates))
                    totalOutput += candidates.GetInt64();
                if (entry.TryGetProperty("cacheRead", out var cacheRead))
                    totalCacheRead += cacheRead.GetInt64();
            }

            usage = new AgentUsage
            {
                InputTokens = (int)totalInput,
                OutputTokens = (int)totalOutput,
                CacheReadTokens = (int)totalCacheRead,
            };
        }

        return [new ResultEvent
        {
            Kind = AgentEventKind.Result,
            Response = response,
            IsSuccess = true,
            Usage = usage,
            RawLine = rawLine,
        }];
    }
}
