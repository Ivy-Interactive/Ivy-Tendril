using System.Text;
using Ivy.Tendril.Helpers;
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
        var configService = UseService<IConfigService>();
        var summaryText = UseState("");
        var reviewAction = UseState("request_changes");

        if (!dialogOpen.Value) return null;

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader($"Agent Review for Plan #{selectedPlan.Id}"),
            new DialogBody(
                Layout.Vertical()
                | Text.P("Submit review comments to the agent on the machine to act upon.")
                | summaryText.ToTextareaInput("Leave a summary comment/instructions...").Rows(4).AutoFocus()
                | reviewAction.ToSelectInput(new Option<string>[]
                {
                    new Option<string>("Implement changes & update plan", "request_changes", description: "Look at comments, implement changes, and come back with a new plan"),
                    new Option<string>("Review implementation as agent", "agent_review", description: "Look at implementation, and leave comments as an agent on the plan")
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
                            var repos = selectedPlan.GetEffectiveRepoPaths(configService);
                            var repoPath = repos.FirstOrDefault() ?? "";

                            sb.AppendLine("Line-by-line feedback:");
                            foreach (var c in draftComments)
                            {
                                var absolutePath = Path.Combine(repoPath, c.FilePath).Replace('\\', '/');
                                var fileLink = $"file:///{absolutePath.TrimStart('/')}";
                                sb.AppendLine($"- **In [{c.FilePath}]({fileLink}#L{c.LineNumber}) line {c.LineNumber}**:");
                                sb.AppendLine($"  {c.Content}");
                            }
                        }

                        var feedback = sb.ToString();
                        if (string.IsNullOrWhiteSpace(feedback))
                        {
                            feedback = "Look at comments, implement changes, and come back with a new plan.";
                        }

                        jobService.StartJob(new RetryPlanArgs(selectedPlan.FolderPath, feedback));
                    }
                    else if (reviewAction.Value == "agent_review")
                    {
                        var instructions = !string.IsNullOrWhiteSpace(summaryText.Value)
                            ? summaryText.Value
                            : "Look at implementation, and leave comments as an agent on the plan.";
                        jobService.StartJob(new UpdatePlanArgs(selectedPlan.FolderPath, Instructions: instructions));
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
