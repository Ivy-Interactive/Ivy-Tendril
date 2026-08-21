using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans;

public class ReviewFeedbackService : IReviewFeedbackService
{
    private readonly ILogger<ReviewFeedbackService> _logger;

    public ReviewFeedbackService(ILogger<ReviewFeedbackService> logger)
    {
        _logger = logger;
    }

    public event Action<ReviewFeedback>? ReviewSubmitted;

    public async Task SaveReviewAsync(string planFolderPath, ReviewFeedback review)
    {
        if (string.IsNullOrWhiteSpace(planFolderPath) || !Directory.Exists(planFolderPath))
        {
            _logger.LogWarning("Cannot save review: plan folder does not exist ({Path})", planFolderPath);
            return;
        }

        var reviewsDir = Path.Combine(planFolderPath, "Artifacts", "reviews");
        Directory.CreateDirectory(reviewsDir);

        if (string.IsNullOrEmpty(review.Id))
        {
            review.Id = Guid.NewGuid().ToString("N")[..8];
        }
        if (review.CreatedAt == default)
        {
            review.CreatedAt = DateTime.UtcNow;
        }

        var filePath = Path.Combine(reviewsDir, $"review_{review.Id}.yaml");
        var yaml = YamlHelper.SerializerCompact.Serialize(review);

        await File.WriteAllTextAsync(filePath, yaml);
        _logger.LogInformation("Saved review {ReviewId} by {Author} to {FilePath}", review.Id, review.Author, filePath);

        ReviewSubmitted?.Invoke(review);
    }

    public List<ReviewFeedback> GetReviewsForPlan(string planFolderPath)
    {
        var result = new List<ReviewFeedback>();
        if (string.IsNullOrWhiteSpace(planFolderPath) || !Directory.Exists(planFolderPath))
            return result;

        var reviewsDir = Path.Combine(planFolderPath, "Artifacts", "reviews");
        if (!Directory.Exists(reviewsDir))
            return result;

        var files = Directory.GetFiles(reviewsDir, "review_*.yaml");
        foreach (var file in files)
        {
            try
            {
                var content = File.ReadAllText(file);
                var review = YamlHelper.Deserializer.Deserialize<ReviewFeedback>(content);
                if (review != null)
                {
                    if (string.IsNullOrEmpty(review.Id))
                    {
                        var filename = Path.GetFileNameWithoutExtension(file);
                        review.Id = filename.StartsWith("review_") ? filename[7..] : filename;
                    }
                    result.Add(review);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load review from {FilePath}", file);
            }
        }

        return result.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public Task DeleteReviewAsync(string planFolderPath, string reviewId)
    {
        if (string.IsNullOrWhiteSpace(planFolderPath) || string.IsNullOrWhiteSpace(reviewId))
            return Task.CompletedTask;

        var reviewsDir = Path.Combine(planFolderPath, "Artifacts", "reviews");
        var filePath = Path.Combine(reviewsDir, $"review_{reviewId}.yaml");

        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted review {ReviewId} at {FilePath}", reviewId, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete review file {FilePath}", filePath);
            }
        }

        return Task.CompletedTask;
    }
}
