using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs.Dialogs;

/// <summary>
/// Dialog shown when rerunning a failed/timed-out/stopped job. Lets the user
/// optionally provide corrective feedback for the agent. For plan execution jobs
/// the feedback is turned into a <see cref="RetryPlanArgs"/> change request; for
/// update jobs it becomes new update instructions. When no feedback is provided the
/// job is rerun with its original arguments.
/// </summary>
public class RerunJobDialog(
    IState<bool> dialogOpen,
    JobItem job,
    IJobService jobService,
    Action onRerun) : ViewBase
{
    public override object? Build()
    {
        var feedback = UseState("");
        if (!dialogOpen.Value) return null;

        var supportsFeedback = SupportsFeedback(job.TypedArgs);

        void Close() => dialogOpen.Set(false);

        var body = supportsFeedback
            ? Layout.Vertical()
              | Text.P("Optionally tell the agent what went wrong or what to do differently. Leave empty to rerun unchanged.")
              | feedback.ToTextareaInput("Feedback for the agent (optional)...").Rows(6).AutoFocus()
            : Layout.Vertical()
              | Text.P("Rerun this job with its original arguments?");

        return new Dialog(
            _ => Close(),
            new DialogHeader($"Rerun {job.Type}"),
            new DialogBody(body),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(Close),
                new Button("Rerun").Primary().Icon(Icons.RotateCw).ShortcutKey("Ctrl+Enter").OnClick(() =>
                {
                    Rerun(feedback.Value);
                    Close();
                })
            )
        ).Width(Size.Rem(30));
    }

    private void Rerun(string feedbackText)
    {
        if (job.TypedArgs == null) return;

        var newArgs = BuildRerunArgs(job.TypedArgs, feedbackText);

        // Plan state transition (and pre-state snapshot) is handled centrally by
        // JobService.StartJob.
        jobService.DeleteJob(job.Id);
        jobService.StartJob(newArgs);
        onRerun();
    }

    internal static bool SupportsFeedback(JobArgsBase? args) =>
        args is ExecutePlanArgs or RetryPlanArgs or UpdatePlanArgs;

    internal static JobArgsBase BuildRerunArgs(JobArgsBase original, string? feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback))
            return original;

        return original switch
        {
            ExecutePlanArgs e => new RetryPlanArgs(e.FolderPath, feedback),
            RetryPlanArgs r => new RetryPlanArgs(r.FolderPath, feedback),
            UpdatePlanArgs u => new UpdatePlanArgs(u.FolderPath, feedback),
            _ => original
        };
    }
}
