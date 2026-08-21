using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

public interface IReviewFeedbackService
{
    event Action<ReviewFeedback>? ReviewSubmitted;
    Task SaveReviewAsync(string planFolderPath, ReviewFeedback review);
    List<ReviewFeedback> GetReviewsForPlan(string planFolderPath);
    Task DeleteReviewAsync(string planFolderPath, string reviewId);
}
