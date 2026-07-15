using Ivy.Plugins;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Plugins;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Plugins;

public class MessagingChannelServiceTests
{
    private class FakeChannel : ITendrilMessagingChannel
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TendrilNotification> Notified { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ITendrilApi? Api { get; private set; }

        public string Id => "fake";

        public Task StartAsync(ITendrilApi api, CancellationToken cancellationToken)
        {
            Api = api;
            Started.TrySetResult();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendNotificationAsync(TendrilNotification notification, CancellationToken cancellationToken)
        {
            Notified.TrySetResult(notification);
            return Task.CompletedTask;
        }
    }

    private class FakePluginServiceProvider(params object[] services) : IPluginServiceProvider
    {
        public T? GetService<T>() where T : class => services.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetServices<T>() where T : class => services.OfType<T>();
    }

    private class FakePluginManager : IPluginManager
    {
        public IReadOnlyList<string> GetActivePluginIds() => [];
        public PluginManifest? GetPluginManifest(string pluginId) => null;
        public PluginConfigurationSchema? GetPluginSchema(string pluginId) => null;
        public object? BuildPluginConfigurationView(string pluginId, IIvyPluginConfig config) => null;
        public IReadOnlyList<PluginCandidate> GetUnloadedPlugins() => [];
        public IReadOnlyList<UnconfiguredPlugin> GetUnconfiguredPlugins() => [];
        public bool UnloadPlugin(string pluginId) => false;
        public bool LoadPlugin(string pluginPath) => false;
        public bool ReloadPlugin(string pluginId) => false;
        public bool ReconfigurePlugin(string pluginId) => false;
        public event Action<string>? PluginLoaded { add { } remove { } }
        public event Action<string>? PluginUnloaded;
        public event Action<string>? PluginReloaded { add { } remove { } }
        public event Action<string>? PluginActivated { add { } remove { } }
        public event Action<string>? PluginDeactivated { add { } remove { } }

        public void RaiseUnloaded(string pluginId) => PluginUnloaded?.Invoke(pluginId);
    }

    private class FakeJobService : IJobService
    {
        public event Action? JobsChanged { add { } remove { } }
        public event Action? JobsStructureChanged { add { } remove { } }
        public event Action? JobPropertyChanged { add { } remove { } }
        public event Action<JobNotification>? NotificationReady;

        public void RaiseNotification(JobNotification notification) => NotificationReady?.Invoke(notification);

        public string StartJob(JobArgsBase args, string? inboxFilePath = null) => "job-1";
        public void ForceStartJob(string id) { }
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public List<JobItem> GetJobs() => [];
        public List<JobItem> GetJobsForPlan(string planFile) => [];
        public JobItem? GetJob(string id) => null;
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => false;
        public bool ReportJobFailure(string id, string message) => false;
        public bool IsInboxFileTracked(string filePath) => false;
        public void Dispose() { }
    }

    private class FakeTendrilApi : ITendrilApi
    {
        public string StartCreatePlan(string description, string? project = null) => "job-1";
        public string StartExecutePlan(string planId, string? note = null) => "job-2";
        public TendrilJobStatus? GetJob(string jobId) => null;
        public IReadOnlyList<TendrilPlanSummary> ListPlans(string? state = null, string? project = null, int limit = 20) => [];
        public IReadOnlyList<string> ListProjects() => [];
    }

    [Fact]
    public async Task Start_StartsRegisteredChannelsWithApi()
    {
        var channel = new FakeChannel();
        var api = new FakeTendrilApi();
        using var service = new MessagingChannelService(
            new FakePluginServiceProvider(channel),
            new FakePluginManager(),
            new FakeJobService(),
            api,
            NullLogger<MessagingChannelService>.Instance);

        service.Start();

        await channel.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(api, channel.Api);
    }

    [Fact]
    public async Task NotificationReady_FansOutToRunningChannels()
    {
        var channel = new FakeChannel();
        var jobService = new FakeJobService();
        using var service = new MessagingChannelService(
            new FakePluginServiceProvider(channel),
            new FakePluginManager(),
            jobService,
            new FakeTendrilApi(),
            NullLogger<MessagingChannelService>.Instance);

        service.Start();
        await channel.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        jobService.RaiseNotification(new JobNotification("CreatePlan Completed", "00042-Plan", true));

        var notification = await channel.Notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("CreatePlan Completed", notification.Title);
        Assert.True(notification.IsSuccess);
    }

    [Fact]
    public async Task PluginUnloaded_StopsRemovedChannels()
    {
        var channel = new FakeChannel();
        var provider = new RemovablePluginServiceProvider(channel);
        var pluginManager = new FakePluginManager();
        var jobService = new FakeJobService();
        using var service = new MessagingChannelService(
            provider,
            pluginManager,
            jobService,
            new FakeTendrilApi(),
            NullLogger<MessagingChannelService>.Instance);

        service.Start();
        await channel.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        provider.Clear();
        pluginManager.RaiseUnloaded("some-plugin");

        jobService.RaiseNotification(new JobNotification("Title", "Message", true));
        await Task.Delay(200);
        Assert.False(channel.Notified.Task.IsCompleted);
    }

    private class RemovablePluginServiceProvider(params object[] services) : IPluginServiceProvider
    {
        private readonly List<object> _services = services.ToList();
        public void Clear() => _services.Clear();
        public T? GetService<T>() where T : class => _services.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetServices<T>() where T : class => _services.OfType<T>().ToList();
    }
}
