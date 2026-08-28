using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding.Helpers;

internal static class OnboardingRepoHelper
{
    public static async Task<List<RepoRef>?> ResolveReposAsync(
        List<RepoRef> selectedRepos,
        string tendrilHome,
        string projectName,
        IState<string?> progressMessage,
        IState<string?> error,
        IState<bool> isCloning,
        IState<bool> isStepLoading)
    {
        var refs = new List<RepoRef>();

        var total = selectedRepos.Count;
        var i = 0;
        foreach (var repo in selectedRepos)
        {
            i++;
            var normalizedPath = RepoPathValidator.Normalize(repo.Path);
            var kind = RepoPathValidator.Classify(normalizedPath);
            if (kind == RepoPathKind.LocalPath)
            {
                progressMessage.Set($"Adding {normalizedPath} ({i}/{total})...");
                var trimmed = normalizedPath.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    refs.Add(repo with { Path = trimmed });
            }
            else
            {
                var owner = RepoPathValidator.ExtractOwnerName(normalizedPath) ?? "default";
                var repoName = RepoPathValidator.ExtractRepoName(normalizedPath) ?? Guid.NewGuid().ToString();
                var destPath = ProjectPathHelper.GetRepoPath(tendrilHome, projectName, owner, repoName);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                progressMessage.Set($"Fetching {owner}/{repoName} ({i}/{total})...");
                var success = await ProcessCheckHelper.CloneRepositoryAsync(normalizedPath, destPath);
                if (!success)
                {
                    error.Set($"Failed to fetch repository: {normalizedPath}.");
                    isCloning.Set(false);
                    isStepLoading.Set(false);
                    return null;
                }
                refs.Add(repo with { Path = destPath });
            }
        }

        return refs;
    }
}
