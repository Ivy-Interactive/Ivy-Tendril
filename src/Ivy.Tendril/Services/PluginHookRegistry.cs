using Ivy.Plugins.Hooks;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class PluginHookRegistry : IPluginHooks
{
    private static readonly TimeSpan HookTimeout = TimeSpan.FromSeconds(10);

    private readonly List<(string PluginId, Func<BeforeJobEvent, CancellationToken, Task> Handler)> _beforeJob = [];
    private readonly List<(string PluginId, Func<AfterJobEvent, CancellationToken, Task> Handler)> _afterJob = [];
    private readonly List<(string PluginId, Func<BeforeCreatePlanEvent, CancellationToken, Task> Handler)> _beforeCreatePlan = [];
    private readonly List<(string PluginId, Func<AfterCreatePlanEvent, CancellationToken, Task> Handler)> _afterCreatePlan = [];
    private readonly List<(string PluginId, Action<ConfigSaveEvent> Handler)> _beforeConfigSave = [];
    private readonly List<(string PluginId, Action Handler)> _afterConfigReload = [];

    private readonly ILogger _logger;
    private readonly Func<string?> _getCurrentPluginId;

    public PluginHookRegistry(ILogger logger, Func<string?> getCurrentPluginId)
    {
        _logger = logger;
        _getCurrentPluginId = getCurrentPluginId;
    }

    public void BeforeJob(Func<BeforeJobEvent, CancellationToken, Task> handler)
        => _beforeJob.Add((_getCurrentPluginId() ?? "__unknown__", handler));

    public void AfterJob(Func<AfterJobEvent, CancellationToken, Task> handler)
        => _afterJob.Add((_getCurrentPluginId() ?? "__unknown__", handler));

    public void BeforeCreatePlan(Func<BeforeCreatePlanEvent, CancellationToken, Task> handler)
        => _beforeCreatePlan.Add((_getCurrentPluginId() ?? "__unknown__", handler));

    public void AfterCreatePlan(Func<AfterCreatePlanEvent, CancellationToken, Task> handler)
        => _afterCreatePlan.Add((_getCurrentPluginId() ?? "__unknown__", handler));

    public void BeforeConfigSave(Action<ConfigSaveEvent> handler)
        => _beforeConfigSave.Add((_getCurrentPluginId() ?? "__unknown__", handler));

    public void AfterConfigReload(Action handler)
        => _afterConfigReload.Add((_getCurrentPluginId() ?? "__unknown__", handler));

    internal void RemovePluginHooks(string pluginId)
    {
        _beforeJob.RemoveAll(h => h.PluginId == pluginId);
        _afterJob.RemoveAll(h => h.PluginId == pluginId);
        _beforeCreatePlan.RemoveAll(h => h.PluginId == pluginId);
        _afterCreatePlan.RemoveAll(h => h.PluginId == pluginId);
        _beforeConfigSave.RemoveAll(h => h.PluginId == pluginId);
        _afterConfigReload.RemoveAll(h => h.PluginId == pluginId);
    }

    internal async Task FireBeforeJobAsync(BeforeJobEvent evt)
    {
        foreach (var (pluginId, handler) in _beforeJob)
        {
            await InvokeAsync(pluginId, "BeforeJob", ct => handler(evt, ct));
            if (evt.Cancelled) break;
        }
    }

    internal async Task FireAfterJobAsync(AfterJobEvent evt)
    {
        foreach (var (pluginId, handler) in _afterJob)
            await InvokeAsync(pluginId, "AfterJob", ct => handler(evt, ct));
    }

    internal async Task FireBeforeCreatePlanAsync(BeforeCreatePlanEvent evt)
    {
        foreach (var (pluginId, handler) in _beforeCreatePlan)
        {
            await InvokeAsync(pluginId, "BeforeCreatePlan", ct => handler(evt, ct));
            if (evt.Cancelled) break;
        }
    }

    internal async Task FireAfterCreatePlanAsync(AfterCreatePlanEvent evt)
    {
        foreach (var (pluginId, handler) in _afterCreatePlan)
            await InvokeAsync(pluginId, "AfterCreatePlan", ct => handler(evt, ct));
    }

    internal void FireBeforeConfigSave(ConfigSaveEvent evt)
    {
        foreach (var (pluginId, handler) in _beforeConfigSave)
        {
            try
            {
                handler(evt);
                if (evt.Rejected) break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Plugin '{PluginId}' BeforeConfigSave hook threw an exception", pluginId);
            }
        }
    }

    internal void FireAfterConfigReload()
    {
        foreach (var (pluginId, handler) in _afterConfigReload)
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Plugin '{PluginId}' AfterConfigReload hook threw an exception", pluginId);
            }
        }
    }

    private async Task InvokeAsync(string pluginId, string hookName, Func<CancellationToken, Task> action)
    {
        using var cts = new CancellationTokenSource(HookTimeout);
        try
        {
            await action(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning("Plugin '{PluginId}' {HookName} hook timed out after {Timeout}s",
                pluginId, hookName, HookTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Plugin '{PluginId}' {HookName} hook threw an exception", pluginId, hookName);
        }
    }
}
