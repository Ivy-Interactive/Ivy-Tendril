using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Services.Plans;

public interface IPlanDiffCommentService
{
    event Action<string, List<DraftComment>>? CommentsChanged;
    List<DraftComment> GetDraftCommentsForPlan(string planFolderPath);
    Task SaveDraftCommentsAsync(string planFolderPath, IEnumerable<DraftComment> comments);
    Task ClearDraftCommentsAsync(string planFolderPath);
}
