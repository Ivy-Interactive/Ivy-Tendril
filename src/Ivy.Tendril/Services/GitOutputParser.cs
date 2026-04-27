namespace Ivy.Tendril.Services;

internal static class GitOutputParser
{
    public static List<(string Status, string FilePath)> ParseNameStatusOutput(string output)
    {
        var files = new List<(string Status, string FilePath)>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2)
                files.Add((parts[0].Trim(), parts[1].Trim()));
        }
        return files;
    }

    public static List<WorktreeInfo> ParseWorktreeList(string output)
    {
        var worktrees = new List<WorktreeInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string? currentPath = null;
        string? currentBranch = null;
        string? currentHash = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("worktree "))
            {
                if (IsWorktreeComplete(currentPath, currentBranch, currentHash))
                    worktrees.Add(new WorktreeInfo(currentPath!, currentBranch!, currentHash!));

                currentPath = line.Substring(9).Trim();
                currentBranch = null;
                currentHash = null;
            }
            else if (line.StartsWith("HEAD "))
                currentHash = line.Substring(5).Trim();
            else if (line.StartsWith("branch "))
            {
                var branchRef = line.Substring(7).Trim();
                currentBranch = branchRef.Replace("refs/heads/", "");
            }
        }

        if (IsWorktreeComplete(currentPath, currentBranch, currentHash))
            worktrees.Add(new WorktreeInfo(currentPath!, currentBranch!, currentHash!));

        return worktrees;
    }

    private static bool IsWorktreeComplete(string? path, string? branch, string? hash)
        => path != null && branch != null && hash != null;

    public static Dictionary<string, (string Title, int FileCount)> ParseCommitSummaries(
        string output,
        HashSet<string> inputHashes)
    {
        var result = new Dictionary<string, (string Title, int FileCount)>();
        string? currentHash = null;
        string? currentTitle = null;
        int currentFileCount = 0;

        foreach (var line in output.Split('\n'))
        {
            if (line.Contains('\0'))
            {
                if (currentHash != null)
                    StoreCommitResult(result, inputHashes, currentHash, currentTitle!, currentFileCount);

                var parts = line.Split('\0', 2);
                currentHash = parts[0].Trim();
                currentTitle = parts.Length > 1 ? parts[1].Trim() : "";
                currentFileCount = 0;
            }
            else if (currentHash != null && line.Trim().Length > 0)
            {
                currentFileCount++;
            }
        }

        if (currentHash != null)
            StoreCommitResult(result, inputHashes, currentHash, currentTitle!, currentFileCount);

        return result;
    }

    private static void StoreCommitResult(
        Dictionary<string, (string Title, int FileCount)> result,
        HashSet<string> inputHashes,
        string fullHash,
        string title,
        int fileCount)
    {
        var value = (title, fileCount);
        result[fullHash] = value;

        foreach (var input in inputHashes)
        {
            if (input.Length < fullHash.Length &&
                fullHash.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                result[input] = value;
            }
        }
    }
}
