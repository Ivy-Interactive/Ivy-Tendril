using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Apps.Review.Tabs;

public class ReviewsTabView(
    List<ReviewFeedback> reviews,
    PlanFile plan,
    IReviewFeedbackService reviewFeedbackService,
    Action refreshPlans) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();

        if (reviews.Count == 0)
        {
            return Layout.Vertical().AlignContent(Align.Center)
                | Text.H3("No Reviews Yet")
                | Text.Muted("Teammates reviewing through the Share Tunnel can submit feedback, which will appear here.");
        }

        var list = Layout.Vertical();

        foreach (var review in reviews)
        {
            var header = Layout.Horizontal().AlignContent(Align.Left)
                | Text.Block(review.Author).Bold()
                | Text.Muted(review.CreatedAt.ToLocalTime().ToString("g")).Small();

            var cardContent = Layout.Vertical();

            if (!string.IsNullOrWhiteSpace(review.Summary))
            {
                cardContent |= Text.Block(review.Summary);
            }

            if (review.DiffComments.Count > 0)
            {
                cardContent |= Text.Block($"Line Comments ({review.DiffComments.Count}):").Bold().Small();
                var commentsLayout = Layout.Vertical();
                foreach (var c in review.DiffComments)
                {
                    commentsLayout |= Layout.Vertical()
                        | Text.Rich()
                            .Bold(c.FilePath, word: true)
                            .Muted($" (line {c.LineNumber})", word: true).Small()
                        | Text.Block(c.Content).Small();
                }
                cardContent |= commentsLayout;
            }

            var deleteBtn = new Button().Icon(Icons.Trash).Ghost().Small()
                .OnClick(async () =>
                {
                    await reviewFeedbackService.DeleteReviewAsync(plan.FolderPath, review.Id);
                    client.Toast("Review deleted", "Deleted");
                    refreshPlans();
                });

            list |= new Card(cardContent).Header(header, null, deleteBtn);
        }

        return list;
    }
}
