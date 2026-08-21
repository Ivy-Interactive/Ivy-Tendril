using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Services.Plans;

public interface IDraftDiffCommentService
{
    List<DraftComment> GetDraftCommentsForPlan(string planFolderPath);
    Task SaveDraftCommentsAsync(string planFolderPath, IEnumerable<DraftComment> comments);
    Task ClearDraftCommentsAsync(string planFolderPath);
}
