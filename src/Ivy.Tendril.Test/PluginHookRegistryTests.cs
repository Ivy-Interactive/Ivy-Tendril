using Ivy.Plugins.Hooks;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class PluginHookRegistryTests
{
    private PluginHookRegistry CreateRegistry(string? pluginId = "test-plugin")
    {
        return new PluginHookRegistry(NullLogger.Instance, () => pluginId);
    }

    [Fact]
    public async Task BeforeJob_HandlerIsCalled()
    {
        var registry = CreateRegistry();
        var called = false;
        registry.BeforeJob((evt, ct) => { called = true; return Task.CompletedTask; });

        await registry.FireBeforeJobAsync(new BeforeJobEvent
        {
            JobId = "j1", JobType = "CreatePlan", PlanFolder = "", Project = "Test"
        });

        Assert.True(called);
    }

    [Fact]
    public async Task AfterJob_HandlerIsCalled()
    {
        var registry = CreateRegistry();
        var called = false;
        registry.AfterJob((evt, ct) => { called = true; return Task.CompletedTask; });

        await registry.FireAfterJobAsync(new AfterJobEvent
        {
            JobId = "j1", JobType = "CreatePlan", Status = Ivy.Plugins.Hooks.JobStatus.Completed,
            PlanFolder = "/plans/001", Project = "Test"
        });

        Assert.True(called);
    }

    [Fact]
    public async Task BeforeJob_Cancel_StopsSubsequentHandlers()
    {
        var registry = CreateRegistry();
        var secondCalled = false;

        registry.BeforeJob((evt, ct) => { evt.Cancel("nope"); return Task.CompletedTask; });
        registry.BeforeJob((evt, ct) => { secondCalled = true; return Task.CompletedTask; });

        var e = new BeforeJobEvent { JobId = "j1", JobType = "ExecutePlan", PlanFolder = "", Project = "Test" };
        await registry.FireBeforeJobAsync(e);

        Assert.True(e.Cancelled);
        Assert.Equal("nope", e.CancellationReason);
        Assert.False(secondCalled);
    }

    [Fact]
    public async Task AfterJob_ExceptionInHandler_DoesNotPropagateAndCallsNextHandler()
    {
        var registry = CreateRegistry();
        var secondCalled = false;

        registry.AfterJob((evt, ct) => throw new InvalidOperationException("boom"));
        registry.AfterJob((evt, ct) => { secondCalled = true; return Task.CompletedTask; });

        await registry.FireAfterJobAsync(new AfterJobEvent
        {
            JobId = "j1", JobType = "CreatePlan", Status = Ivy.Plugins.Hooks.JobStatus.Failed,
            PlanFolder = "", Project = "Test"
        });

        Assert.True(secondCalled);
    }

    [Fact]
    public async Task BeforeJob_Timeout_DoesNotBlock()
    {
        var registry = CreateRegistry();
        registry.BeforeJob(async (evt, ct) => await Task.Delay(TimeSpan.FromSeconds(30), ct));

        // Should complete within the 10-second timeout (plus some margin)
        var task = registry.FireBeforeJobAsync(new BeforeJobEvent
        {
            JobId = "j1", JobType = "CreatePlan", PlanFolder = "", Project = "Test"
        });

        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void BeforeConfigSave_HandlerIsCalled()
    {
        var registry = CreateRegistry();
        var called = false;
        registry.BeforeConfigSave(evt => called = true);

        registry.FireBeforeConfigSave(new ConfigSaveEvent
        {
            CurrentSettings = new object(),
            NewSettings = new object()
        });

        Assert.True(called);
    }

    [Fact]
    public void BeforeConfigSave_Reject_StopsSubsequentHandlers()
    {
        var registry = CreateRegistry();
        var secondCalled = false;

        registry.BeforeConfigSave(evt => evt.Reject("invalid"));
        registry.BeforeConfigSave(evt => secondCalled = true);

        var e = new ConfigSaveEvent { CurrentSettings = new object(), NewSettings = new object() };
        registry.FireBeforeConfigSave(e);

        Assert.True(e.Rejected);
        Assert.Equal("invalid", e.RejectionReason);
        Assert.False(secondCalled);
    }

    [Fact]
    public void AfterConfigReload_HandlerIsCalled()
    {
        var registry = CreateRegistry();
        var called = false;
        registry.AfterConfigReload(() => called = true);

        registry.FireAfterConfigReload();

        Assert.True(called);
    }

    [Fact]
    public void RemovePluginHooks_RemovesOnlyThatPlugin()
    {
        string? currentPlugin = "plugin-a";
        var registry = new PluginHookRegistry(NullLogger.Instance, () => currentPlugin);

        var aCalled = false;
        var bCalled = false;

        registry.AfterConfigReload(() => aCalled = true);

        currentPlugin = "plugin-b";
        registry.AfterConfigReload(() => bCalled = true);

        registry.RemovePluginHooks("plugin-a");
        registry.FireAfterConfigReload();

        Assert.False(aCalled);
        Assert.True(bCalled);
    }

    [Fact]
    public async Task BeforeCreatePlan_MutableDescription()
    {
        var registry = CreateRegistry();
        registry.BeforeCreatePlan((evt, ct) =>
        {
            evt.Description = "enriched: " + evt.Description;
            return Task.CompletedTask;
        });

        var e = new BeforeCreatePlanEvent
        {
            Description = "fix bug",
            Project = "Auto"
        };
        await registry.FireBeforeCreatePlanAsync(e);

        Assert.Equal("enriched: fix bug", e.Description);
    }
}
