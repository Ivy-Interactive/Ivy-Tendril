using System.Text;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Gemini;

/// <summary>
/// Parses Gemini CLI output. Gemini outputs a single JSON blob (not JSONL),
/// so all lines are accumulated and parsed on Flush().
/// </summary>
public sealed class GeminiEventParser : IEventParser
{
    public string AgentId => Abstractions.AgentId.Gemini;

    private static readonly IReadOnlyList<AgentEvent> Empty = Array.Empty<AgentEvent>();
    private readonly StringBuilder _buffer = new();

    public IReadOnlyList<AgentEvent> ParseLine(string rawLine)
    {
        _buffer.AppendLine(rawLine);
        return Empty;
    }

    public IReadOnlyList<AgentEvent> Flush()
    {
        var json = _buffer.ToString().Trim();
        if (string.IsNullOrEmpty(json))
            return Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var events = new List<AgentEvent>();

            // Extract session_id and model from stats
            var sessionId = root.TryGetProperty("session_id", out var sidProp)
                ? sidProp.GetString() ?? ""
                : "";

            string? model = null;
            long totalLatencyMs = 0;
            int inputTokens = 0;
            int outputTokens = 0;
            int cacheReadTokens = 0;
            int thinkingTokens = 0;

            if (root.TryGetProperty("stats", out var stats) &&
                stats.TryGetProperty("models", out var models) &&
                models.ValueKind == JsonValueKind.Object)
            {
                foreach (var modelEntry in models.EnumerateObject())
                {
                    model ??= modelEntry.Name;

                    if (modelEntry.Value.TryGetProperty("api", out var api))
                    {
                        totalLatencyMs += api.TryGetProperty("totalLatencyMs", out var latency)
                            ? latency.GetInt64()
                            : 0;
                    }

                    if (modelEntry.Value.TryGetProperty("tokens", out var tokens))
                    {
                        inputTokens += tokens.TryGetProperty("input", out var inp) ? inp.GetInt32() : 0;
                        outputTokens += tokens.TryGetProperty("candidates", out var cand) ? cand.GetInt32() : 0;
                        cacheReadTokens += tokens.TryGetProperty("cached", out var cached) ? cached.GetInt32() : 0;
                        thinkingTokens += tokens.TryGetProperty("thoughts", out var thoughts) ? thoughts.GetInt32() : 0;
                    }
                }
            }

            // SessionInitEvent
            events.Add(new SessionInitEvent
            {
                Kind = AgentEventKind.SessionInit,
                SessionId = sessionId,
                Model = model,
                RawLine = json,
            });

            // TextEvent from response
            var response = root.TryGetProperty("response", out var respProp)
                ? respProp.GetString()
                : null;

            if (!string.IsNullOrEmpty(response))
            {
                events.Add(new TextEvent
                {
                    Kind = AgentEventKind.Text,
                    Text = response,
                    RawLine = json,
                });
            }

            // ResultEvent with usage
            var usage = new AgentUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheReadTokens = cacheReadTokens,
                ReasoningTokens = thinkingTokens,
            };

            events.Add(new ResultEvent
            {
                Kind = AgentEventKind.Result,
                Response = response,
                IsSuccess = !string.IsNullOrEmpty(response),
                Duration = totalLatencyMs > 0 ? TimeSpan.FromMilliseconds(totalLatencyMs) : null,
                Usage = usage,
                RawLine = json,
            });

            return events;
        }
        catch (JsonException)
        {
            return [new UnknownEvent { Kind = AgentEventKind.Unknown, Content = json, RawLine = json }];
        }
    }

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

    public void Reset()
    {
        _buffer.Clear();
    }
}
