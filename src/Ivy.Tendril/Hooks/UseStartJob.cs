using Ivy.Tendril.Services;

namespace Ivy.Tendril.Hooks;

public static class UseStartJobExtensions
{
    /// <summary>
    /// Wraps IJobService.StartJob with automatic race condition protection.
    /// Returns a safe startJob action and isStarting flag for button state.
    /// </summary>
    /// <param name="context">The view context</param>
    /// <returns>A tuple containing the startJob action and isStarting flag</returns>
    public static (Action<string, string[]> StartJob, bool IsStarting) UseStartJob(
        this IViewContext context)
    {
        var jobService = context.UseService<IJobService>();
        var isStarting = context.UseState(false);

        Action<string, string[]> startJob = (type, args) =>
        {
            if (!isStarting.Value)
            {
                isStarting.Set(true);
                jobService.StartJob(type, args);
                // Note: isStarting stays true - dialogs close immediately after StartJob,
                // so resetting to false is not needed
            }
        };

        return (startJob, isStarting.Value);
    }
}
