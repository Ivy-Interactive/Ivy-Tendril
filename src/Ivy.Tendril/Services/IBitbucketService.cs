namespace Ivy.Tendril.Services;

public interface IBitbucketService
{
    Task<(Dictionary<string, string> statuses, string? error)> GetPrStatusesAsync(string workspace, string repoSlug, List<string> prUrls);
    Task<(List<string> assignees, string? error)> GetAssigneesAsync(string workspace, string repoSlug);
    Task<(List<string> labels, string? error)> GetLabelsAsync(string workspace, string repoSlug);
}
