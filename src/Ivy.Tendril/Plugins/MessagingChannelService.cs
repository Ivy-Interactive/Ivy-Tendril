using Ivy.Plugins;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Plugins;

public class MessagingChannelService(
    IPluginServiceProvider pluginServiceProvider,
    IPluginManager pluginManager,
    IJobService jobService,
    ITendrilApi tendrilApi,
    ILogger<MessagingChannelService> logger,
    TendrilPluginConfigFactory? configFactory = null) : IStartable, IDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<ITendrilMessagingChannel, CancellationTokenSource> _runningChannels = new();
    private bool _started;

    public void Start()
    {
        lock (_lock)
        {
            if (_started) return;
            _started = true;
        }

        jobService.NotificationReady += OnNotificationReady;
        pluginManager.PluginActivated += OnPluginStateChanged;
        pluginManager.PluginDeactivated += OnPluginStateChanged;
        pluginManager.PluginUnloaded += OnPluginStateChanged;
        pluginManager.PluginReloaded += OnPluginStateChanged;
        if (configFactory != null)
            configFactory.ConfigSaved += OnPluginStateChanged;

        SyncChannels();
    }

    private void OnPluginStateChanged(string pluginId) => SyncChannels();

    private void SyncChannels()
    {
        List<ITendrilMessagingChannel> current;
        try
        {
            current = pluginServiceProvider.GetServices<ITendrilMessagingChannel>().ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve messaging channels from plugins");
            return;
        }

        List<(ITendrilMessagingChannel Channel, CancellationTokenSource Cts)> toStop = new();
        List<(ITendrilMessagingChannel Channel, CancellationTokenSource Cts)> toStart = new();

        lock (_lock)
        {
            foreach (var (channel, cts) in _runningChannels.Where(kv => !current.Contains(kv.Key)).ToList())
            {
                _runningChannels.Remove(channel);
                toStop.Add((channel, cts));
            }

            foreach (var channel in current.Where(c => !_runningChannels.ContainsKey(c)))
            {
                var cts = new CancellationTokenSource();
                _runningChannels[channel] = cts;
                toStart.Add((channel, cts));
            }
        }

        foreach (var (channel, cts) in toStop)
            _ = StopChannelAsync(channel, cts);

        foreach (var (channel, cts) in toStart)
        {
            var token = cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("Starting messaging channel {ChannelId}", channel.Id);
                    await channel.StartAsync(tendrilApi, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Messaging channel {ChannelId} failed to start", channel.Id);
                }
            }, CancellationToken.None);
        }
    }

    private async Task StopChannelAsync(ITendrilMessagingChannel channel, CancellationTokenSource cts)
    {
        try
        {
            logger.LogInformation("Stopping messaging channel {ChannelId}", channel.Id);
            await cts.CancelAsync();
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await channel.StopAsync(stopTimeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Messaging channel {ChannelId} failed to stop cleanly", channel.Id);
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void OnNotificationReady(JobNotification notification)
    {
        List<ITendrilMessagingChannel> channels;
        lock (_lock)
            channels = _runningChannels.Keys.ToList();

        if (channels.Count == 0) return;

        var tendrilNotification = new TendrilNotification(notification.Title, notification.Message, notification.IsSuccess);
        foreach (var channel in channels)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await channel.SendNotificationAsync(tendrilNotification, sendTimeout.Token);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Messaging channel {ChannelId} failed to send notification", channel.Id);
                }
            });
        }
    }

    public void Dispose()
    {
        jobService.NotificationReady -= OnNotificationReady;
        pluginManager.PluginActivated -= OnPluginStateChanged;
        pluginManager.PluginDeactivated -= OnPluginStateChanged;
        pluginManager.PluginUnloaded -= OnPluginStateChanged;
        pluginManager.PluginReloaded -= OnPluginStateChanged;
        if (configFactory != null)
            configFactory.ConfigSaved -= OnPluginStateChanged;

        List<(ITendrilMessagingChannel Channel, CancellationTokenSource Cts)> running;
        lock (_lock)
        {
            running = _runningChannels.Select(kv => (kv.Key, kv.Value)).ToList();
            _runningChannels.Clear();
        }
        foreach (var (channel, cts) in running)
            StopChannelAsync(channel, cts).GetAwaiter().GetResult();
    }
}
