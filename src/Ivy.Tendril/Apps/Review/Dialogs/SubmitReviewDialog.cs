using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Share;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Review.Dialogs;

public class SubmitReviewDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IShareContext shareContext,
    IReviewFeedbackService reviewFeedbackService,
    List<DraftComment>? draftComments = null,
    IState<List<DraftComment>>? draftCommentsState = null,
    Action? onSubmitted = null) : ViewBase
{
    public override object? Build()
    {
        var client = UseService<IClientProvider>();
        var summaryText = UseState("");
        var isSubmitting = UseState(false);
        var persona = UseState(shareContext.Persona);

        if (!dialogOpen.Value) return null;

        var commentCount = draftComments?.Count ?? 0;

        void HandleSubmit()
        {
            if (isSubmitting.Value) return;
            if (commentCount == 0 && string.IsNullOrWhiteSpace(summaryText.Value)) return;
            isSubmitting.Set(true);

            var review = new ReviewFeedback
            {
                Author = persona.Value,
                PlanFolder = selectedPlan.FolderPath,
                Summary = summaryText.Value.Trim(),
                DiffComments = draftComments?.Select(c => new ReviewCommentItem
                {
                    FilePath = c.FilePath,
                    LineNumber = c.LineNumber,
                    Content = c.Content
                }).ToList() ?? []
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    await reviewFeedbackService.SaveReviewAsync(selectedPlan.FolderPath, review);
                    client.Toast($"Review submitted as {persona.Value}!", "Feedback Sent");
                    draftCommentsState?.Set([]);
                    onSubmitted?.Invoke();
                    dialogOpen.Set(false);
                }
                catch (Exception ex)
                {
                    client.Toast($"Failed to submit review: {ex.Message}", "Error").Destructive();
                }
                finally
                {
                    isSubmitting.Set(false);
                }
            });
        }

        var submitLabel = commentCount > 0
            ? $"Submit Review ({commentCount} inline comments)"
            : "Submit Review";

        var body = Layout.Vertical()
            | Text.P($"Your review will be shared with the plan author under the persona below.")
            | persona.ToTextInput().WithField().Label("Reviewer Persona")
            | (commentCount > 0
                ? Callout.Info($"{commentCount} inline comment(s) on file diffs will be attached to this review.")
                : null)
            | summaryText.ToTextareaInput("Add any overall comments, questions, or suggestions...").Rows(4)
                .WithField().Label("Overall Feedback")
            | new Button(submitLabel).Primary()
                .Disabled(isSubmitting.Value || (commentCount == 0 && string.IsNullOrWhiteSpace(summaryText.Value)))
                .OnClick(HandleSubmit);

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader($"Submit Review for Plan #{selectedPlan.Id}"),
            new DialogBody(body)
        ).Width(Size.Rem(32));
    }
}
