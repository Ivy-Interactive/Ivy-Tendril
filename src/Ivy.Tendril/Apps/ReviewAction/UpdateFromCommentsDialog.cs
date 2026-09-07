using System.Collections.Immutable;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.ReviewAction;

/// <summary>
///     Confirms sending the comments left on the running app back to the agent as a change
///     request — the same <see cref="RetryPlanArgs"/> path as "Request Changes" on the plan's
///     diff, so this feedback arrives exactly like line-by-line feedback does.
///
///     It asks first rather than firing on the button because the agent then rewrites the
///     branch: worth one look at what is about to be sent, and the list is the only place the
///     reviewer sees all their comments together.
/// </summary>
public class UpdateFromCommentsDialog(
    IState<bool> dialogOpen,
    PlanFile plan,
    string appUrl,
    IState<ImmutableList<AppComment>> comments,
    IJobService jobService,
    IPlanReaderService planService,
    Action onSubmitted) : ViewBase
{
    public override object? Build()
    {
        // Before the early returns below: Build stops rendering entirely while the dialog is
        // closed, and a hook that only runs on some renders is a hook whose state drifts.
        var client = UseService<IClientProvider>();

        if (!dialogOpen.Value) return null;

        var pending = comments.Value;
        if (pending.IsEmpty)
        {
            dialogOpen.Set(false);
            return null;
        }


        // Read the plan fresh rather than trusting the one this view was handed: that is a
        // snapshot from when the review action opened, and a review session outlives several
        // jobs. Cheap here because it only runs while the dialog is actually open.
        var current = planService.GetPlanByFolder(plan.FolderPath) ?? plan;
        var planJobs = jobService.GetJobsForPlan(plan.FolderName);

        if (!AppPreview.CanRequestChanges(current.Status, planJobs))
        {
            return new Dialog(
                _ => dialogOpen.Set(false),
                new DialogHeader($"Plan #{plan.Id} is not taking changes"),
                new DialogBody(
                    Layout.Vertical().Gap(2)
                    | Text.P($"The plan is {current.Status}. A change request lands only while it is in Review, or while it is already applying one.")
                    | Text.Muted($"The {pending.Count} comment(s) are still here — send them once the plan is back in Review.")
                ),
                new DialogFooter(
                    new Button("Close").Outline().OnClick(() => dialogOpen.Set(false))
                )
            ).Width(Size.Rem(32));
        }

        var waitFor = AppPreview.JobsToWaitFor(planJobs);

        // Grouped by page, exactly as the change request will be: the reviewer sees what the
        // agent is about to be told, and a comment left three screens back is not presented as
        // though it were about the page they happen to be looking at.
        var lines = pending
            .GroupBy(comment => comment.Url ?? appUrl)
            .Select(page => (object)(Layout.Vertical().Gap(1)
                | Text.Strong(page.Key)
                | (Layout.Vertical().Gap(2) | page.Select(comment =>
                {
                    var where = AppPreview.SourceLabel(comment.DebugJson);
                    var tag = string.IsNullOrEmpty(comment.Tag) ? "element" : comment.Tag;
                    return (object)(Layout.Vertical().Gap(0)
                        | Text.Block($"{comment.Number}. {comment.Comment}")
                        | Text.Muted(where is not null ? $"{tag} · {where}" : $"{tag} · {comment.Selector}"));
                }))));

        var body = Layout.Vertical().Gap(2)
            | Text.P($"{pending.Count} comment(s) from the running app will be sent to the agent as a change request.");

        if (waitFor.Count > 0)
        {
            body |= Text.Muted(
                $"Plan #{plan.Id} already has {waitFor.Count} job(s) in flight, so this one queues behind them "
                + "and starts when they finish.");
        }

        body |= (Layout.Vertical().Gap(2) | lines);

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader($"Update Plan #{plan.Id}"),
            new DialogBody(body),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button(waitFor.Count > 0 ? "Queue Update" : "Update").ShortcutKey("Enter").AutoFocus().OnClick(() =>
                {
                    // Re-read at the click. The dialog may have been open a while, and this is
                    // the cheap guard against posting a change request into a plan that has
                    // moved on, or against missing a job that started in the meantime.
                    var latest = planService.GetPlanByFolder(plan.FolderPath) ?? plan;
                    var latestJobs = jobService.GetJobsForPlan(plan.FolderName);

                    if (!AppPreview.CanRequestChanges(latest.Status, latestJobs))
                    {
                        client.Toast(
                            $"Plan #{plan.Id} is {latest.Status} and cannot take a change request now. Your comments are kept.",
                            "Not Sent");
                        dialogOpen.Set(false);
                        return;
                    }

                    var chain = AppPreview.JobsToWaitFor(latestJobs);
                    jobService.StartJob(new RetryPlanArgs(
                        plan.FolderPath,
                        AppPreview.FormatChangeRequest(appUrl, pending))
                    {
                        WaitForJobs = chain.Count > 0 ? chain : null,
                    });
                    onSubmitted();
                    dialogOpen.Set(false);
                })
            )
        ).Width(Size.Rem(32));
    }
}
