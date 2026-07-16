using System.Text;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Review.Dialogs;

public class SubmitReviewDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    List<DraftComment> draftComments,
    IState<List<DraftComment>> draftCommentsState,
    IJobService jobService,
    IPlanReaderService planService,
    Action refreshPlans) : ViewBase
{
    public override object? Build()
    {
        var summaryText = UseState("");
        var reviewAction = UseState("request_changes");

        if (!dialogOpen.Value) return null;

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader($"Submit Review for Plan #{selectedPlan.Id}"),
            new DialogBody(
                Layout.Vertical()
                | Text.P("Submit review comments to the agent on the machine to act upon.")
                | summaryText.ToTextareaInput("Leave a summary comment/instructions...").Rows(4).AutoFocus()
                | reviewAction.ToSelectInput(new Option<string>[]
                {
                    new Option<string>("Request Changes (Run Agent)", "request_changes", description: "Submit comments and trigger agent retry"),
                    new Option<string>("Approve (Complete Plan)", "approve", description: "Mark the plan as completed"),
                    new Option<string>("Comment Only", "comment", description: "Keep comments as reference without starting job")
                }).Radio()
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button("Submit").Primary().OnClick(() =>
                {
                    if (reviewAction.Value == "request_changes")
                    {
                        var sb = new StringBuilder();
                        if (!string.IsNullOrWhiteSpace(summaryText.Value))
                        {
                            sb.AppendLine(summaryText.Value);
                            sb.AppendLine();
                        }
                        if (draftComments.Count > 0)
                        {
                            sb.AppendLine("Line-by-line feedback:");
                            foreach (var c in draftComments)
                            {
                                sb.AppendLine($"- **In `{c.FilePath}` line {c.LineNumber}**:");
                                sb.AppendLine($"  {c.Content}");
                            }
                        }

                        var feedback = sb.ToString();
                        if (string.IsNullOrWhiteSpace(feedback))
                        {
                            feedback = "Review submitted with no specific feedback.";
                        }

                        jobService.StartJob(new RetryPlanArgs(selectedPlan.FolderPath, feedback));
                    }
                    else if (reviewAction.Value == "approve")
                    {
                        planService.TransitionState(selectedPlan.FolderName, PlanStatus.Completed);
                        WorktreeCleanupService.RemoveWorktreesInBackground(selectedPlan.FolderPath);
                    }

                    // Clear draft comments
                    draftCommentsState.Set(new List<DraftComment>());

                    refreshPlans();
                    dialogOpen.Set(false);
                })
            )
        ).Width(Size.Rem(32));
    }
}
