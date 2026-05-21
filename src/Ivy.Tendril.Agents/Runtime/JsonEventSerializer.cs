using System.Text.Json;
using System.Text.Json.Serialization;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class JsonEventSerializer : IEventSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolverChain = { AgentJsonContext.Default },
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string Serialize(AgentEvent evt)
    {
        var wire = ToWire(evt);
        return JsonSerializer.Serialize(wire, wire.GetType(), WriteOptions);
    }

    public AgentEvent? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("kind", out var kindProp))
                return null;

            var kind = kindProp.GetString();
            var timestamp = root.TryGetProperty("timestamp", out var ts)
                ? ts.GetString() ?? DateTimeOffset.UtcNow.ToString("o")
                : DateTimeOffset.UtcNow.ToString("o");

            return kind switch
            {
                "session_init" => DeserializeSessionInit(root, timestamp),
                "text" => DeserializeText(root, timestamp),
                "thinking" => DeserializeThinking(root, timestamp),
                "tool_call" => DeserializeToolCall(root, timestamp),
                "tool_result" => DeserializeToolResult(root, timestamp),
                "permission_request" => DeserializePermissionRequest(root, timestamp),
                "permission_denial" => DeserializePermissionDenial(root, timestamp),
                "error" => DeserializeError(root, timestamp),
                "result" => DeserializeResult(root, timestamp),
                "file_change" => DeserializeFileChange(root, timestamp),
                "user_question" => DeserializeUserQuestion(root, timestamp),
                _ => new UnknownEvent
                {
                    Kind = AgentEventKind.Unknown,
                    Content = json,
                    Timestamp = DateTimeOffset.Parse(timestamp),
                }
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static EventWire ToWire(AgentEvent evt)
    {
        var ts = evt.Timestamp.ToString("o");

        return evt switch
        {
            SessionInitEvent e => new SessionInitWire
            {
                Timestamp = ts,
                SessionId = e.SessionId,
                Model = e.Model,
                Tools = e.AvailableTools,
            },
            TextEvent e => new TextWire
            {
                Timestamp = ts,
                Text = e.Text,
                Delta = e.IsDelta,
            },
            ThinkingEvent e => new ThinkingWire
            {
                Timestamp = ts,
                Content = e.Content,
            },
            ToolCallEvent e => new ToolCallWire
            {
                Timestamp = ts,
                ToolUseId = e.ToolUseId,
                ToolName = e.ToolName,
                Input = e.InputJson is not null ? JsonDocument.Parse(e.InputJson).RootElement : null,
            },
            ToolResultEvent e => new ToolResultWire
            {
                Timestamp = ts,
                ToolUseId = e.ToolUseId,
                ToolName = e.ToolName,
                Output = e.Output,
                IsError = e.IsError,
            },
            PermissionRequestEvent e => new PermissionRequestWire
            {
                Timestamp = ts,
                RequestId = e.RequestId,
                ToolName = e.ToolName,
                Description = e.Description,
                Input = e.Input,
                IsDestructive = e.IsDestructive,
                Pattern = e.Pattern,
            },
            PermissionDenialEvent e => new PermissionDenialWire
            {
                Timestamp = ts,
                ToolName = e.ToolName,
                InputSummary = e.InputSummary,
            },
            ErrorEvent e => new ErrorWire
            {
                Timestamp = ts,
                Message = e.Message,
                Code = e.Code,
                IsRetryable = e.IsRetryable,
                IsAuthError = e.IsAuthError,
            },
            ResultEvent e => new ResultWire
            {
                Timestamp = ts,
                Response = e.Response,
                IsSuccess = e.IsSuccess,
                DurationMs = e.Duration.HasValue ? (long)e.Duration.Value.TotalMilliseconds : null,
                TurnCount = e.TurnCount,
                ExitCode = e.ExitCode,
                Usage = e.Usage is not null ? ToUsageWire(e.Usage) : null,
                PermissionDenials = e.PermissionDenials.Count > 0
                    ? e.PermissionDenials.Select(d => new PermissionDenialWire
                    {
                        Timestamp = d.Timestamp.ToString("o"),
                        ToolName = d.ToolName,
                        InputSummary = d.InputSummary,
                    }).ToList()
                    : null,
            },
            FileChangeEvent e => new FileChangeWire
            {
                Timestamp = ts,
                FilePath = e.FilePath,
                ChangeKind = e.ChangeKind.ToString().ToLowerInvariant(),
                LinesAdded = e.LinesAdded,
                LinesRemoved = e.LinesRemoved,
            },
            UserQuestionEvent e => new UserQuestionWire
            {
                Timestamp = ts,
                QuestionId = e.QuestionId,
                Question = e.Question,
                Options = e.Options?.Select(o => new QuestionOptionWire
                {
                    Label = o.Label,
                    Value = o.Value,
                    Description = o.Description,
                }).ToList(),
                MultiSelect = e.AllowMultiSelect,
                Description = e.Description,
                IsBlocking = e.IsBlocking,
                TimeoutMs = e.Timeout.HasValue ? (long)e.Timeout.Value.TotalMilliseconds : null,
            },
            _ => new TextWire
            {
                Timestamp = ts,
                Text = evt.ToString() ?? "",
            },
        };
    }

    private static UsageWire ToUsageWire(AgentUsage usage) => new()
    {
        InputTokens = usage.InputTokens,
        OutputTokens = usage.OutputTokens,
        CacheReadTokens = usage.CacheReadTokens,
        CacheWriteTokens = usage.CacheWriteTokens,
        ReasoningTokens = usage.ReasoningTokens,
        CostUsd = usage.CostUsd,
        PremiumRequests = usage.PremiumRequests,
        Model = usage.Model,
        ModelBreakdown = usage.ModelBreakdown?.Select(m => new UsageWire
        {
            InputTokens = m.InputTokens,
            OutputTokens = m.OutputTokens,
            CacheReadTokens = m.CacheReadTokens,
            CacheWriteTokens = m.CacheWriteTokens,
            CostUsd = m.CostUsd,
            Model = m.Model,
        }).ToList(),
    };

    private static SessionInitEvent DeserializeSessionInit(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.SessionInit,
        Timestamp = DateTimeOffset.Parse(timestamp),
        SessionId = root.TryGetProperty("session_id", out var sid) ? sid.GetString()! : "",
        Model = root.TryGetProperty("model", out var m) ? m.GetString() : null,
        AvailableTools = root.TryGetProperty("tools", out var tools)
            ? tools.EnumerateArray().Select(t => t.GetString()!).ToList()
            : null,
    };

    private static TextEvent DeserializeText(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.Text,
        Timestamp = DateTimeOffset.Parse(timestamp),
        Text = root.TryGetProperty("text", out var t) ? t.GetString()! : "",
        IsDelta = root.TryGetProperty("delta", out var d) && d.GetBoolean(),
    };

    private static ThinkingEvent DeserializeThinking(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.Thinking,
        Timestamp = DateTimeOffset.Parse(timestamp),
        Content = root.TryGetProperty("content", out var c) ? c.GetString()! : "",
    };

    private static ToolCallEvent DeserializeToolCall(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.ToolCall,
        Timestamp = DateTimeOffset.Parse(timestamp),
        ToolUseId = root.TryGetProperty("tool_use_id", out var id) ? id.GetString()! : "",
        ToolName = root.TryGetProperty("tool_name", out var name) ? name.GetString()! : "",
        InputJson = root.TryGetProperty("input", out var input) ? input.GetRawText() : null,
    };

    private static ToolResultEvent DeserializeToolResult(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.ToolResult,
        Timestamp = DateTimeOffset.Parse(timestamp),
        ToolUseId = root.TryGetProperty("tool_use_id", out var id) ? id.GetString()! : "",
        ToolName = root.TryGetProperty("tool_name", out var name) ? name.GetString() : null,
        Output = root.TryGetProperty("output", out var o) ? o.GetString() : null,
        IsError = root.TryGetProperty("is_error", out var e) && e.GetBoolean(),
    };

    private static PermissionRequestEvent DeserializePermissionRequest(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.PermissionRequest,
        Timestamp = DateTimeOffset.Parse(timestamp),
        RequestId = root.TryGetProperty("request_id", out var id) ? id.GetString()! : "",
        ToolName = root.TryGetProperty("tool_name", out var name) ? name.GetString()! : "",
        Description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null,
        Input = root.TryGetProperty("input", out var input) ? input.GetString() : null,
        IsDestructive = root.TryGetProperty("is_destructive", out var d) && d.GetBoolean(),
        Pattern = root.TryGetProperty("pattern", out var p) ? p.GetString() : null,
    };

    private static PermissionDenialEvent DeserializePermissionDenial(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.PermissionDenial,
        Timestamp = DateTimeOffset.Parse(timestamp),
        ToolName = root.TryGetProperty("tool_name", out var name) ? name.GetString()! : "",
        InputSummary = root.TryGetProperty("input_summary", out var s) ? s.GetString() : null,
    };

    private static ErrorEvent DeserializeError(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.Error,
        Timestamp = DateTimeOffset.Parse(timestamp),
        Message = root.TryGetProperty("message", out var m) ? m.GetString()! : "",
        Code = root.TryGetProperty("code", out var c) ? c.GetString() : null,
        IsRetryable = root.TryGetProperty("is_retryable", out var r) && r.GetBoolean(),
        IsAuthError = root.TryGetProperty("is_auth_error", out var a) && a.GetBoolean(),
    };

    private static ResultEvent DeserializeResult(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.Result,
        Timestamp = DateTimeOffset.Parse(timestamp),
        Response = root.TryGetProperty("response", out var resp) ? resp.GetString() : null,
        IsSuccess = root.TryGetProperty("is_success", out var s) && s.GetBoolean(),
        Duration = root.TryGetProperty("duration_ms", out var dur)
            ? TimeSpan.FromMilliseconds(dur.GetInt64())
            : null,
        TurnCount = root.TryGetProperty("turn_count", out var tc) ? tc.GetInt32() : null,
        ExitCode = root.TryGetProperty("exit_code", out var ec) ? ec.GetInt32() : null,
        Usage = root.TryGetProperty("usage", out var usage) ? DeserializeUsage(usage) : null,
    };

    private static FileChangeEvent DeserializeFileChange(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.FileChange,
        Timestamp = DateTimeOffset.Parse(timestamp),
        FilePath = root.TryGetProperty("file_path", out var fp) ? fp.GetString()! : "",
        ChangeKind = root.TryGetProperty("change_kind", out var ck)
            ? Enum.TryParse<FileChangeKind>(ck.GetString(), ignoreCase: true, out var parsed)
                ? parsed
                : FileChangeKind.Modified
            : FileChangeKind.Modified,
        LinesAdded = root.TryGetProperty("lines_added", out var la) ? la.GetInt32() : 0,
        LinesRemoved = root.TryGetProperty("lines_removed", out var lr) ? lr.GetInt32() : 0,
    };

    private static UserQuestionEvent DeserializeUserQuestion(JsonElement root, string timestamp) => new()
    {
        Kind = AgentEventKind.UserQuestion,
        Timestamp = DateTimeOffset.Parse(timestamp),
        QuestionId = root.TryGetProperty("question_id", out var id) ? id.GetString()! : "",
        Question = root.TryGetProperty("question", out var q) ? q.GetString()! : "",
        Options = root.TryGetProperty("options", out var opts)
            ? opts.EnumerateArray().Select(o => new QuestionOption
            {
                Label = o.TryGetProperty("label", out var l) ? l.GetString()! : "",
                Value = o.TryGetProperty("value", out var v) ? v.GetString()! : "",
                Description = o.TryGetProperty("description", out var d) ? d.GetString() : null,
            }).ToList()
            : null,
        AllowMultiSelect = root.TryGetProperty("multi_select", out var ms) && ms.GetBoolean(),
        Description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null,
        IsBlocking = root.TryGetProperty("is_blocking", out var b) && b.GetBoolean(),
        Timeout = root.TryGetProperty("timeout_ms", out var to) ? TimeSpan.FromMilliseconds(to.GetInt64()) : null,
    };

    private static AgentUsage DeserializeUsage(JsonElement el) => new()
    {
        InputTokens = el.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
        OutputTokens = el.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
        CacheReadTokens = el.TryGetProperty("cache_read_tokens", out var cr) ? cr.GetInt32() : 0,
        CacheWriteTokens = el.TryGetProperty("cache_write_tokens", out var cw) ? cw.GetInt32() : 0,
        ReasoningTokens = el.TryGetProperty("reasoning_tokens", out var rt) ? rt.GetInt32() : 0,
        CostUsd = el.TryGetProperty("cost_usd", out var cost) ? cost.GetDecimal() : null,
        PremiumRequests = el.TryGetProperty("premium_requests", out var pr) ? pr.GetInt32() : null,
        Model = el.TryGetProperty("model", out var m) ? m.GetString() : null,
    };
}
