using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class ReviewFeedbackServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly ReviewFeedbackService _service;

    public ReviewFeedbackServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "tendril_review_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _service = new ReviewFeedbackService(NullLogger<ReviewFeedbackService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task SaveReviewAsync_WritesYamlFile_AndTriggersEvent()
    {
        ReviewFeedback? eventPayload = null;
        _service.ReviewSubmitted += review => eventPayload = review;

        var review = new ReviewFeedback
        {
            Author = "Curious Otter",
            PlanFolder = _testDir,
            Summary = "Looks fantastic, ready to ship.",
            DiffComments = new List<ReviewCommentItem>
            {
                new ReviewCommentItem
                {
                    FilePath = "src/Main.cs",
                    LineNumber = 42,
                    Content = "Consider caching this result."
                }
            }
        };

        await _service.SaveReviewAsync(_testDir, review);

        Assert.NotNull(review.Id);
        Assert.Equal("Curious Otter", review.Author);
        Assert.NotNull(eventPayload);
        Assert.Equal(review.Id, eventPayload.Id);

        var reviewsDir = Path.Combine(_testDir, "Artifacts", "reviews");
        Assert.True(Directory.Exists(reviewsDir));

        var loaded = _service.GetReviewsForPlan(_testDir);
        Assert.Single(loaded);
        Assert.Equal("Curious Otter", loaded[0].Author);
        Assert.Equal("Looks fantastic, ready to ship.", loaded[0].Summary);
        Assert.Single(loaded[0].DiffComments);
        Assert.Equal(42, loaded[0].DiffComments[0].LineNumber);
    }

    [Fact]
    public async Task DeleteReviewAsync_RemovesFile()
    {
        var review = new ReviewFeedback
        {
            Author = "Spectating Zebra",
            PlanFolder = _testDir,
            Summary = "Needs refactoring."
        };

        await _service.SaveReviewAsync(_testDir, review);
        var loadedBefore = _service.GetReviewsForPlan(_testDir);
        Assert.Single(loadedBefore);

        await _service.DeleteReviewAsync(_testDir, review.Id);

        var loadedAfter = _service.GetReviewsForPlan(_testDir);
        Assert.Empty(loadedAfter);
    }
}
