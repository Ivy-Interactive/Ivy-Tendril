using System;
using System.Collections.Generic;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityEventParser : IEventParser
{
    public string AgentId => Abstractions.AgentId.Antigravity;

    private static readonly IReadOnlyList<AgentEvent> Empty = Array.Empty<AgentEvent>();

    public IReadOnlyList<AgentEvent> ParseLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine)) return Empty;
        var trimmed = rawLine.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return [new TextEvent { Kind = AgentEventKind.Text, Text = rawLine + "\n", RawLine = rawLine }];
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (!root.TryGetProperty("event", out var evtProp))
            {
                return [new TextEvent { Kind = AgentEventKind.Text, Text = rawLine + "\n", RawLine = rawLine }];
            }

            var evtType = evtProp.GetString();
            return evtType switch
            {
                "init" => ParseInit(root, rawLine),
                "step_update" => ParseStepUpdate(root, rawLine),
                "result" => ParseResult(root, rawLine),
                _ => Empty
            };
        }
        catch (JsonException)
        {
            return [new TextEvent { Kind = AgentEventKind.Text, Text = rawLine + "\n", RawLine = rawLine }];
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

    public IEventParser CreateFresh() => new AntigravityEventParser();

    private static IReadOnlyList<AgentEvent> ParseInit(JsonElement root, string rawLine)
    {
        var convId = root.TryGetProperty("conversation_id", out var cid) ? cid.GetString() ?? "" : "";
        string? model = null;
        var tools = new List<string>();

        if (root.TryGetProperty("init", out var initEl))
        {
            if (initEl.TryGetProperty("model", out var m)) model = m.GetString();
            if (initEl.TryGetProperty("tools", out var tArr) && tArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tArr.EnumerateArray())
                {
                    var name = t.GetString();
                    if (name is not null) tools.Add(name);
                }
            }
        }

        return [new SessionInitEvent
        {
            Kind = AgentEventKind.SessionInit,
            SessionId = convId,
            Model = model,
            AvailableTools = tools,
            RawLine = rawLine,
        }];
    }

    private static IReadOnlyList<AgentEvent> ParseStepUpdate(JsonElement root, string rawLine)
    {
        if (!root.TryGetProperty("step_update", out var su)) return Empty;

        var stepType = su.TryGetProperty("step_type", out var st) ? st.GetString() : null;
        var state = su.TryGetProperty("state", out var s) ? s.GetString() : null;
        var stepIndex = su.TryGetProperty("step_index", out var idx) ? idx.GetInt32().ToString() : "0";

        if (stepType == "tool")
        {
            var toolName = su.TryGetProperty("tool_name", out var tn) ? tn.GetString() ?? "unknown" : "unknown";
            string? paramsJson = null;
            string? description = null;

            if (su.TryGetProperty("tool_info", out var ti))
            {
                if (ti.TryGetProperty("parameters", out var p))
                {
                    paramsJson = p.GetRawText();
                }
                if (ti.TryGetProperty("description", out var d))
                {
                    description = d.GetString();
                }
            }

            if (state == "ACTIVE")
            {
                return [new ToolCallEvent
                {
                    Kind = AgentEventKind.ToolCall,
                    ToolUseId = stepIndex,
                    ToolName = toolName,
                    InputJson = paramsJson,
                    Description = description,
                    RawLine = rawLine,
                }];
            }
            else if (state == "DONE")
            {
                string? output = null;
                bool isError = false;

                if (su.TryGetProperty("tool_info", out var tiDone))
                {
                    if (tiDone.TryGetProperty("output", out var outProp))
                    {
                        output = outProp.ValueKind == JsonValueKind.String ? outProp.GetString() : outProp.GetRawText();
                    }
                    else if (tiDone.TryGetProperty("error", out var errProp))
                    {
                        output = errProp.ValueKind == JsonValueKind.String ? errProp.GetString() : errProp.GetRawText();
                        isError = true;
                    }
                }

                return [new ToolResultEvent
                {
                    Kind = AgentEventKind.ToolResult,
                    ToolUseId = stepIndex,
                    Output = output,
                    IsError = isError,
                    RawLine = rawLine,
                }];
            }
        }
        else if (stepType == "thinking")
        {
            var thinkingText = su.TryGetProperty("text", out var tp) ? tp.GetString() ?? "" :
                               su.TryGetProperty("thinking_delta", out var td) ? td.GetString() ?? "" : "";
            if (!string.IsNullOrEmpty(thinkingText))
            {
                return [new ThinkingEvent
                {
                    Kind = AgentEventKind.Thinking,
                    Content = thinkingText,
                    RawLine = rawLine,
                }];
            }
        }
        else if (stepType == "agent_response")
        {
            var textDelta = su.TryGetProperty("text_delta", out var td) ? td.GetString() :
                            su.TryGetProperty("text", out var tp) ? tp.GetString() : null;
            if (!string.IsNullOrEmpty(textDelta))
            {
                return [new TextEvent
                {
                    Kind = AgentEventKind.Text,
                    Text = textDelta,
                    IsDelta = true,
                    RawLine = rawLine,
                }];
            }
        }

        return Empty;
    }

    private static IReadOnlyList<AgentEvent> ParseResult(JsonElement root, string rawLine)
    {
        if (!root.TryGetProperty("result", out var res)) return Empty;

        var status = res.TryGetProperty("status", out var sp) ? sp.GetString() : "SUCCESS";
        var isSuccess = !string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase);
        var responseText = res.TryGetProperty("response", out var rp) ? rp.GetString() : null;
        var durationSec = res.TryGetProperty("duration_seconds", out var dp) ? dp.GetDouble() : 0;

        AgentUsage? usage = null;
        if (res.TryGetProperty("usage", out var usageEl))
        {
            usage = new AgentUsage
            {
                InputTokens = usageEl.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                OutputTokens = usageEl.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
                CacheReadTokens = usageEl.TryGetProperty("cache_read_tokens", out var cr) ? cr.GetInt32() : 0,
            };
        }

        return [new ResultEvent
        {
            Kind = AgentEventKind.Result,
            Response = responseText,
            IsSuccess = isSuccess,
            Duration = durationSec > 0 ? TimeSpan.FromSeconds(durationSec) : null,
            Usage = usage,
            RawLine = rawLine,
        }];
    }
}
