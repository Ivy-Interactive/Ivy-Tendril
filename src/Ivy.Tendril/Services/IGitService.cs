namespace Ivy.Tendril.Services;

public interface IGitService
{
    GitResult<string> GetCommitTitle(string repoPath, string commitHash);
    GitResult<string> GetCommitDiff(string repoPath, string commitHash);
    GitResult<List<(string Status, string FilePath)>> GetCommitFiles(string repoPath, string commitHash);
    GitResult<int> GetCommitFileCount(string repoPath, string commitHash);
    GitResult<string> GetCombinedDiff(string repoPath, string firstCommit, string lastCommit);
    GitResult<List<(string Status, string FilePath)>> GetCombinedChangedFiles(string repoPath, string firstCommit, string lastCommit);
    GitResult<List<WorktreeInfo>> GetWorktrees(string repoPath);
    GitResult<Dictionary<string, (string Title, int FileCount)>> GetCommitSummaries(string repoPath, IEnumerable<string> commitHashes);
}

public record WorktreeInfo(string Path, string Branch, string CommitHash);