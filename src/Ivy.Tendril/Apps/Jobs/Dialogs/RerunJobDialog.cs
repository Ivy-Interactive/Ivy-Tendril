using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs.Dialogs;

/// <summary>
/// Dialog shown when rerunning a failed/timed-out/stopped job. Lets the user
/// optionally provide corrective feedback for the agent. For plan execution jobs
/// the feedback is turned into a <see cref="RetryPlanArgs"/> change request; for
/// update jobs it becomes new update instructions. When no feedback is provided the
/// job is rerun with its original arguments (or executed if it produced a plan).
/// </summary>
public class RerunJobDialog(
    IState<bool> dialogOpen,
    JobItem job,
    IJobService jobService,
    Action onRerun) : ViewBase
{
    public override object? Build()
    {
        var planService = UseService<IPlanReaderService>();
        var feedback = UseState("");
        if (!dialogOpen.Value) return null;

        var supportsFeedback = SupportsFeedback(job, planService);

        void Close() => dialogOpen.Set(false);

        var body = supportsFeedback
            ? Layout.Vertical()
              | Text.P("Optionally tell the agent what went wrong or what to do differently. Leave empty to rerun unchanged.")
              | feedback.ToTextareaInput("Feedback for the agent (Optional)").Rows(6).AutoFocus()
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
                    Rerun(feedback.Value, planService);
                    Close();
                })
            )
        ).Width(Size.Rem(30));
    }

    private void Rerun(string feedbackText, IPlanReaderService? planService)
    {
        if (job.TypedArgs == null) return;

        var newArgs = BuildRerunArgs(job, feedbackText, planService);

        // Plan state transition (and pre-state snapshot) is handled centrally by
        // JobService.StartJob.
        jobService.DeleteJob(job.Id);
        jobService.StartJob(newArgs);
        onRerun();
    }

    internal static bool SupportsFeedback(JobItem job, IPlanReaderService? planService = null)
    {
        if (job.TypedArgs == null) return false;
        if (SupportsFeedback(job.TypedArgs)) return true;
        if (job.TypedArgs is CreatePlanArgs && ResolvePlanFolder(job, planService) != null) return true;
        return false;
    }

    internal static bool SupportsFeedback(JobArgsBase? args) =>
        args is ExecutePlanArgs or RetryPlanArgs or UpdatePlanArgs;

    internal static JobArgsBase BuildRerunArgs(JobItem job, string? feedback, IPlanReaderService? planService = null)
    {
        if (job.TypedArgs == null) return new CreatePlanArgs("", "Auto");
        var planFolder = ResolvePlanFolder(job, planService);
        return BuildRerunArgs(job.TypedArgs, feedback, planFolder);
    }

    internal static JobArgsBase BuildRerunArgs(JobArgsBase original, string? feedback, string? planFolder = null)
    {
        if (original is CreatePlanArgs && !string.IsNullOrEmpty(planFolder))
        {
            return !string.IsNullOrWhiteSpace(feedback)
                ? new RetryPlanArgs(planFolder, feedback)
                : new ExecutePlanArgs(planFolder);
        }

        if (string.IsNullOrWhiteSpace(feedback))
            return original;

        return original switch
        {
            ExecutePlanArgs e => new RetryPlanArgs(e.FolderPath, feedback),
            RetryPlanArgs r => new RetryPlanArgs(r.FolderPath, feedback),
            UpdatePlanArgs u => u with { Instructions = feedback },
            _ => original
        };
    }

    internal static string? ResolvePlanFolder(JobItem job, IPlanReaderService? planService = null)
    {
        var plansDir = planService?.PlansDirectory;
        var planId = job.ReportedPlanId ?? job.AllocatedPlanId ?? JobsApp.ExtractPlanId(job.PlanFile);

        if (!string.IsNullOrEmpty(planId) && plansDir != null && Directory.Exists(plansDir))
        {
            var folder = PlanYamlHelper.FindPlanFolderById(plansDir, planId);
            if (folder != null) return Path.Combine(plansDir, folder);
        }

        if (!string.IsNullOrEmpty(job.PlanFile))
        {
            if (Directory.Exists(job.PlanFile))
                return job.PlanFile;

            if (plansDir != null)
            {
                var fullPath = Path.Combine(plansDir, job.PlanFile);
                if (Directory.Exists(fullPath)) return fullPath;
            }

            if (!string.IsNullOrEmpty(planId))
            {
                if (plansDir != null)
                    return Path.Combine(plansDir, job.PlanFile);
                return job.PlanFile;
            }
        }

        return null;
    }
}

