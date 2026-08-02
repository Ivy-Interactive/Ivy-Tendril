using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyEventParser : IEventParser
{
    private readonly OpenCodeEventParser _inner = new();

    public string AgentId => Abstractions.AgentId.OpenAiProxy;

    public IReadOnlyList<AgentEvent> ParseLine(string rawLine)
        => _inner.ParseLine(rawLine);

    public IReadOnlyList<AgentEvent> Flush()
        => _inner.Flush();

    public ResultEvent? BuildResult(IReadOnlyList<AgentEvent> events, int exitCode)
        => _inner.BuildResult(events, exitCode);

    public IEventParser CreateFresh()
        => new OpenAiProxyEventParser();
}
