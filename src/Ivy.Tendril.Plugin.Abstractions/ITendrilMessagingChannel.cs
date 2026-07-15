namespace Ivy.Tendril.Plugins;

public interface ITendrilMessagingChannel
{
    string Id { get; }
    Task StartAsync(ITendrilApi api, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task SendNotificationAsync(TendrilNotification notification, CancellationToken cancellationToken);
}

public record TendrilNotification(string Title, string Message, bool IsSuccess);
