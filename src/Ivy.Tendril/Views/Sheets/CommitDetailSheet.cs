using Ivy.Core;
using Ivy.Tendril.Models;
using Ivy.Tendril.Helpers;

using Ivy.Tendril.Services;
namespace Ivy.Tendril.Views.Sheets;

public class CommitDetailSheet(
    IState<string?> openCommit,
    PlanFile? selectedPlan,
    IConfigService config,
    IGitService gitService) : ViewBase
{
    public override object Build()
    {
        var commitQuery = UseQuery<PlanContentHelpers.CommitDetailData?, string>(
            openCommit.Value ?? "",
            async (hash, ct) =>
            {
                if (string.IsNullOrEmpty(hash) || selectedPlan is null) return null;
                var repoPaths = selectedPlan.GetEffectiveRepoPaths(config);
                return await Task.Run(() =>
                {
                    foreach (var repo in repoPaths)
                    {
                        var titleResult = gitService.GetCommitTitle(repo, hash);
                        if (titleResult.IsSuccess)
                        {
                            var diffResult = gitService.GetCommitDiff(repo, hash);
                            var filesResult = gitService.GetCommitFiles(repo, hash);
                            return new PlanContentHelpers.CommitDetailData(
                                titleResult.Value!,
                                diffResult.IsSuccess ? diffResult.Value : null,
                                filesResult.IsSuccess ? filesResult.Value : null);
                        }
                    }
                    return null;
                }, ct);
            },
            initialValue: null
        );

        if (openCommit.Value is not { } commitHash || selectedPlan is null)
            return new Empty();

        return PlanContentHelpers.RenderCommitDetailSheet(
            commitQuery.Value,
            commitQuery.Loading || commitQuery.Value is null && !string.IsNullOrEmpty(openCommit.Value),
            commitHash,
            () => openCommit.Set(null),
            commitQuery.Error);
    }
}
