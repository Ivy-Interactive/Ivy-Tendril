using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Chat;

internal static class ChatLauncher
{
    public static bool UsesTerminal(IConfigService config) => ChatModes.IsTerminal(config.Settings.ChatMode);

    public static (Type App, object? Args) TargetFor(IConfigService config, string? prompt = null, string? title = null) =>
        UsesTerminal(config)
            ? (typeof(AgentApp), new AgentAppArgs(prompt, title))
            : (typeof(ChatApp), new ChatAppArgs(Prompt: prompt));

    public static void Open(INavigator navigator, IConfigService config, string? prompt = null, string? title = null)
    {
        var (app, args) = TargetFor(config, prompt, title);
        navigator.Navigate(app, args);
    }

    public static (Type App, object? Args) NewSessionTarget(IConfigService config, IChatHistoryService chats, IAgentRunner runner)
    {
        if (UsesTerminal(config)) return (typeof(AgentApp), new AgentAppArgs());

        var agent = config.Settings.CodingAgent ?? "claude";
        var models = ChatApp.GetModelsForAgent(runner, agent);
        var session = chats.CreateSession(agent, models.Count > 0 ? models[0].Id : "default", kind: ChatSessionKinds.Chat);
        return (typeof(ChatApp), new ChatAppArgs(SessionId: session.Id));
    }

    public static void StartNew(INavigator navigator, IConfigService config, IChatHistoryService chats, IAgentRunner runner)
    {
        var (app, args) = NewSessionTarget(config, chats, runner);
        navigator.Navigate(app, args);
    }

    public static string? LatestTerminalSessionId(IEnumerable<ChatSessionModel> sessions) =>
        sessions.Where(s => s.IsTerminal()).OrderByDescending(s => s.UpdatedAt).FirstOrDefault()?.Id;

    public static string SessionListSignature(
        IEnumerable<ChatSessionModel> sessions,
        IReadOnlySet<string>? generatingIds = null,
        IReadOnlySet<string>? completedIds = null) =>
        string.Join("\u001e", sessions.Select(s =>
            $"{s.Id}\u001f{s.Title}\u001f{s.Kind}\u001f{generatingIds?.Contains(s.Id) == true}\u001f{completedIds?.Contains(s.Id) == true}"));
}
