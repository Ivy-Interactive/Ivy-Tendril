using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Hooks;

public static class UseStartJobExtensions
{
    /// <summary>
    /// Wraps IJobService.StartJob with automatic race condition protection.
    /// Returns a safe startJob action and isStarting flag for button state.
    /// </summary>
    /// <param name="context">The view context</param>
    /// <returns>A tuple containing the startJob action and isStarting flag</returns>
    public static (Action<JobArgsBase> StartJob, bool IsStarting) UseStartJob(
        this IViewContext context)
    {
        var jobService = context.UseService<IJobService>();
        var isStarting = context.UseState(false);

        Action<JobArgsBase> startJob = args =>
        {
            if (!isStarting.Value)
            {
                isStarting.Set(true);
                jobService.StartJob(args);
            }
        };

        return (startJob, isStarting.Value);
    }
}
