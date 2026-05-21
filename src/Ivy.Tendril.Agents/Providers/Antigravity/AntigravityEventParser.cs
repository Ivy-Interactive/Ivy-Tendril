using System.Text;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityEventParser : IEventParser
{
    public string AgentId => Abstractions.AgentId.Antigravity;

    private readonly StringBuilder _buffer = new();

    public IReadOnlyList<AgentEvent> ParseLine(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
            _buffer.AppendLine(line);

        return [];
    }

    public IReadOnlyList<AgentEvent> Flush()
    {
        var content = _buffer.ToString().Trim();
        _buffer.Clear();

        if (string.IsNullOrEmpty(content))
            return [];

        var events = new List<AgentEvent>();

        events.Add(new SessionInitEvent
        {
            Kind = AgentEventKind.SessionInit,
            SessionId = "",
        });

        events.Add(new TextEvent
        {
            Kind = AgentEventKind.Text,
            Text = content,
        });

        events.Add(new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = true,
            Response = content,
        });

        return events;
    }

    public ResultEvent? BuildResult(IReadOnlyList<AgentEvent> events, int exitCode)
    {
        var existing = events.OfType<ResultEvent>().LastOrDefault();
        if (existing is not null)
            return existing with { ExitCode = exitCode };

        return new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = exitCode == 0,
            ExitCode = exitCode,
        };
    }

    public void Reset() => _buffer.Clear();
}
