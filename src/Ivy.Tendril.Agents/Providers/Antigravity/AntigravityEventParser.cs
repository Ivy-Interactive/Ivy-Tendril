using System.Text;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityEventParser : IEventParser
{
    public string AgentId => Abstractions.AgentId.Antigravity;

    private bool _initialized;

    public IReadOnlyList<AgentEvent> ParseLine(string line)
    {
        var events = new List<AgentEvent>();
        if (!_initialized)
        {
            _initialized = true;
            events.Add(new SessionInitEvent
            {
                Kind = AgentEventKind.SessionInit,
                SessionId = "",
            });
        }

        if (!string.IsNullOrEmpty(line))
        {
            events.Add(new TextEvent
            {
                Kind = AgentEventKind.Text,
                Text = line + "\n",
                RawLine = line,
            });
        }

        return events;
    }

    public IReadOnlyList<AgentEvent> Flush()
    {
        var events = new List<AgentEvent>();
        if (!_initialized)
        {
            _initialized = true;
            events.Add(new SessionInitEvent
            {
                Kind = AgentEventKind.SessionInit,
                SessionId = "",
            });
        }

        events.Add(new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = true,
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

    public IEventParser CreateFresh() => new AntigravityEventParser();
}
