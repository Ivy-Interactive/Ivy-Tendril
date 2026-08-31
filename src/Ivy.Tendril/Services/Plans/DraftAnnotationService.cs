using Ivy.Tendril.Helpers;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans;

public class DraftAnnotationService : IDraftAnnotationService
{
    private readonly ILogger<DraftAnnotationService> _logger;

    public event Action<string, List<MarkdownAnnotation>>? AnnotationsChanged;

    public DraftAnnotationService(ILogger<DraftAnnotationService> logger)
    {
        _logger = logger;
    }

    public async Task SaveAnnotationsAsync(string planFolderPath, IEnumerable<MarkdownAnnotation> annotations)
    {
        if (string.IsNullOrWhiteSpace(planFolderPath) || !Directory.Exists(planFolderPath))
        {
            _logger.LogWarning("Cannot save draft annotations: plan folder does not exist ({Path})", planFolderPath);
            return;
        }

        var artifactsDir = Path.Combine(planFolderPath, "Artifacts");
        Directory.CreateDirectory(artifactsDir);

        var filePath = Path.Combine(artifactsDir, "draft_annotations.yaml");
        var list = annotations.ToList();

        if (list.Count == 0)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Cleared draft annotations at {FilePath}", filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete empty draft annotations file {FilePath}", filePath);
                }
            }
            AnnotationsChanged?.Invoke(planFolderPath, []);
            return;
        }

        try
        {
            var yaml = YamlHelper.SerializerCompact.Serialize(list);
            await File.WriteAllTextAsync(filePath, yaml);
            _logger.LogInformation("Saved {Count} draft annotations to {FilePath}", list.Count, filePath);
            AnnotationsChanged?.Invoke(planFolderPath, list);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save draft annotations to {FilePath}", filePath);
        }
    }

    public List<MarkdownAnnotation> GetAnnotationsForPlan(string planFolderPath)
    {
        var result = new List<MarkdownAnnotation>();
        if (string.IsNullOrWhiteSpace(planFolderPath) || !Directory.Exists(planFolderPath))
            return result;

        var filePath = Path.Combine(planFolderPath, "Artifacts", "draft_annotations.yaml");
        if (!File.Exists(filePath))
            return result;

        try
        {
            var content = File.ReadAllText(filePath);
            var items = YamlHelper.Deserializer.Deserialize<List<MarkdownAnnotation>>(content);
            if (items != null)
                result.AddRange(items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load draft annotations from {FilePath}", filePath);
        }

        return result;
    }

    public Task ClearAnnotationsAsync(string planFolderPath)
    {
        if (string.IsNullOrWhiteSpace(planFolderPath) || !Directory.Exists(planFolderPath))
            return Task.CompletedTask;

        var filePath = Path.Combine(planFolderPath, "Artifacts", "draft_annotations.yaml");
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted draft annotations file at {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear draft annotations at {FilePath}", filePath);
            }
        }

        AnnotationsChanged?.Invoke(planFolderPath, []);
        return Task.CompletedTask;
    }
}
