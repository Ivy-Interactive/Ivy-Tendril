using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test;

public class WorktreePathBudgetTests
{
    [Fact]
    public void WorstCaseWorktreeRootLength_ReturnsExpectedLengthForNewCap()
    {
        var plansRoot = 17; // D:\.tendril\Plans
        var repoPath = 31; // SpaceCorps\components-storybook

        var result = WorktreePathHelper.WorstCaseWorktreeRootLength(plansRoot, repoPath);

        Assert.Equal(90, result);
    }

    [Fact]
    public void WorstCaseWorktreeRootLength_ReturnsExpectedLengthForLegacyCap()
    {
        var plansRoot = 17; // D:\.tendril\Plans
        var repoPath = 31; // SpaceCorps\components-storybook
        var legacyFolderLength = 66; // 5 (ID) + 1 (dash) + 60 (old SafeTitleMaxLength)

        var result = WorktreePathHelper.WorstCaseWorktreeRootLength(plansRoot, repoPath, legacyFolderLength);

        Assert.Equal(126, result);
    }

    [Theory]
    [InlineData(133, 259)] // components-storybook tsgolint.exe
    [InlineData(138, 259)] // ivy-tendril tsgolint.exe
    [InlineData(133, 246)] // foundry tsgolint.CMD
    [InlineData(148, 246)] // deepest nested components-storybook .CMD
    public void WorstCaseWorktreeRoot_PlusLauncherSuffix_FitsWithinCeiling(int launcherSuffix, int ceiling)
    {
        var worstCaseRoot = 90;

        var totalLength = worstCaseRoot + 1 + launcherSuffix;

        Assert.True(totalLength <= ceiling, $"Total length {totalLength} exceeds ceiling {ceiling}");
    }

    [Fact]
    public void WorstCaseWorktreeRoot_PlusUnreachableLauncher_ExpectedToExceed()
    {
        var worstCaseRoot = 90;
        var unreachableLauncherSuffix = 185; // tsserver.CMD
        var cmdCeiling = 246;

        var totalLength = worstCaseRoot + 1 + unreachableLauncherSuffix;

        Assert.True(totalLength > cmdCeiling, "tsserver.CMD is expected to exceed the ceiling and remain unreachable");
    }

    [Fact]
    public void MaxWorktreeRootLength_IsGreaterThanOrEqualToWorstCaseAtShippedCap()
    {
        var plansRoot = 17;
        var repoPath = 31;
        var worstCase = WorktreePathHelper.WorstCaseWorktreeRootLength(plansRoot, repoPath);

        Assert.True(WorktreePathHelper.MaxWorktreeRootLength >= worstCase,
            $"MaxWorktreeRootLength {WorktreePathHelper.MaxWorktreeRootLength} must be >= worst case {worstCase}");
    }

    [Fact]
    public void TryGetPlanFolderFromWorktree_NestedLayout_ReturnsTrue()
    {
        var plansRoot = Path.Combine(Path.GetTempPath(), "TestPlans");
        var planFolder = Path.Combine(plansRoot, "00214-TestPlan");
        var worktreePath = Path.Combine(planFolder, "Worktrees", "owner", "repo");

        var result = WorktreePathHelper.TryGetPlanFolderFromWorktree(worktreePath, out var recoveredPlanFolder);

        Assert.True(result);
        Assert.Equal(planFolder, recoveredPlanFolder);
    }

    [Fact]
    public void TryGetPlanFolderFromWorktree_FlatLayout_ReturnsTrue()
    {
        var plansRoot = Path.Combine(Path.GetTempPath(), "TestPlans");
        var planFolder = Path.Combine(plansRoot, "00214-TestPlan");
        var worktreePath = Path.Combine(planFolder, "Worktrees", "repo");

        var result = WorktreePathHelper.TryGetPlanFolderFromWorktree(worktreePath, out var recoveredPlanFolder);

        Assert.True(result);
        Assert.Equal(planFolder, recoveredPlanFolder);
    }

    [Fact]
    public void TryGetPlanFolderFromWorktree_NoWorktreesAncestor_ReturnsFalse()
    {
        var pathWithoutWorktrees = Path.Combine(Path.GetTempPath(), "SomeOtherPath", "NotAWorktree");

        var result = WorktreePathHelper.TryGetPlanFolderFromWorktree(pathWithoutWorktrees, out var planFolder);

        Assert.False(result);
        Assert.Equal(string.Empty, planFolder);
    }

    [Fact]
    public void TryGetPlanFolderFromWorktree_WorktreesDirectoryItself_ReturnsFalse()
    {
        var plansRoot = Path.Combine(Path.GetTempPath(), "TestPlans");
        var planFolder = Path.Combine(plansRoot, "00214-TestPlan");
        var worktreesDir = Path.Combine(planFolder, "Worktrees");

        var result = WorktreePathHelper.TryGetPlanFolderFromWorktree(worktreesDir, out var recoveredPlanFolder);

        Assert.False(result);
    }
}
