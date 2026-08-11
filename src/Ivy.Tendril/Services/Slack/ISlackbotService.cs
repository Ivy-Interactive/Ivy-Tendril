namespace Ivy.Tendril.Services.Slack;

public interface ISlackbotService
{
    SlackbotStatus Status { get; }
    string? BotName { get; }
    string? TeamName { get; }
    string? LastError { get; }
    event Action<SlackbotStatus>? StatusChanged;
    Task<SlackAuthInfo> TestConnectionAsync(string botToken, string appToken, CancellationToken ct = default);
    Task RestartAsync();
    Task StopAsync();
}
