using Ivy.Tendril.Helpers;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Plans;

public class DraftDiffCommentService : IDraftDiffCommentService
{
    private const string CommentsFileName = "draft_diff_comments.yaml";
    private readonly ILogger<DraftDiffCommentService> _logger;

    public event Action<string, List<DraftComment>>? CommentsChanged;

    public DraftDiffCommentService(ILogger<DraftDiffCommentService> logger)
    {
        _logger = logger;
    }

    public List<DraftComment> GetDraftCommentsForPlan(string planFolderPath)
    {
        if (string.IsNullOrWhiteSpace(planFolderPath) || !Directory.Exists(planFolderPath))
            return [];

        var filePath = Path.Combine(planFolderPath, "Artifacts", CommentsFileName);
        if (!File.Exists(filePath))
            return [];

        try
        {
            var content = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(content))
                return [];

            var loaded = YamlHelper.Deserializer.Deserialize<List<DraftComment>>(content);
            return loaded ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize draft diff comments from {FilePath}", filePath);
            return [];
        }
    }

    public async Task SaveDraftCommentsAsync(string planFolderPath, IEnumerable<DraftComment> comments)
    {
        if (string.IsNullOrWhiteSpace(planFolderPath) || !Directory.Exists(planFolderPath))
        {
            _logger.LogWarning("Cannot save draft diff comments: plan folder does not exist ({Path})", planFolderPath);
            return;
        }

        var artifactsDir = Path.Combine(planFolderPath, "Artifacts");
        Directory.CreateDirectory(artifactsDir);

        var filePath = Path.Combine(artifactsDir, CommentsFileName);
        var list = comments.ToList();

        if (list.Count == 0)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Deleted empty draft diff comments file at {FilePath}", filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete empty draft diff comments file at {FilePath}", filePath);
                }
            }
            CommentsChanged?.Invoke(planFolderPath, []);
            return;
        }

        var yaml = YamlHelper.SerializerCompact.Serialize(list);
        await File.WriteAllTextAsync(filePath, yaml);
        _logger.LogInformation("Saved {Count} draft diff comments to {FilePath}", list.Count, filePath);
        CommentsChanged?.Invoke(planFolderPath, list);
    }

    public Task ClearDraftCommentsAsync(string planFolderPath)
    {
        if (string.IsNullOrWhiteSpace(planFolderPath))
            return Task.CompletedTask;

        var filePath = Path.Combine(planFolderPath, "Artifacts", CommentsFileName);
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                _logger.LogInformation("Cleared draft diff comments at {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear draft diff comments at {FilePath}", filePath);
            }
        }

        CommentsChanged?.Invoke(planFolderPath, []);
        return Task.CompletedTask;
    }
}
