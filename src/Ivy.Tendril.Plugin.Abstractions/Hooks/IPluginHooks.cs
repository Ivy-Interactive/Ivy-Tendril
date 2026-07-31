namespace Ivy.Plugins.Hooks;

/// <summary>
/// Allows plugins to register in-process callbacks that fire at specific points
/// in the Tendril lifecycle.
/// </summary>
public interface IPluginHooks
{
    /// <summary>Fire before a job starts executing.</summary>
    void BeforeJob(Func<BeforeJobEvent, CancellationToken, Task> handler);

    /// <summary>Fire after a job completes (success or failure).</summary>
    void AfterJob(Func<AfterJobEvent, CancellationToken, Task> handler);

    /// <summary>Fire before a plan is created (from inbox, UI, or API).</summary>
    void BeforeCreatePlan(Func<BeforeCreatePlanEvent, CancellationToken, Task> handler);

    /// <summary>Fire after a plan has been successfully created.</summary>
    void AfterCreatePlan(Func<AfterCreatePlanEvent, CancellationToken, Task> handler);

    /// <summary>Fire when config is about to be saved.</summary>
    void BeforeConfigSave(Action<ConfigSaveEvent> handler);

    /// <summary>Fire after config has been reloaded.</summary>
    void AfterConfigReload(Action handler);
}
