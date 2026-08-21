using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Services.Plans;

public interface IDraftAnnotationService
{
    Task SaveAnnotationsAsync(string planFolderPath, IEnumerable<MarkdownAnnotation> annotations);
    List<MarkdownAnnotation> GetAnnotationsForPlan(string planFolderPath);
    Task ClearAnnotationsAsync(string planFolderPath);
}
