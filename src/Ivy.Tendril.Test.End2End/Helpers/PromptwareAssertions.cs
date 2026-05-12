namespace Ivy.Tendril.Test.End2End.Helpers;

public static class PromptwareAssertions
{
    public static void AssertPlanYamlExists(string planFolder)
    {
        var yamlPath = Path.Combine(planFolder, "plan.yaml");
        Assert.True(File.Exists(yamlPath),
            $"plan.yaml not found at {yamlPath}. Folder contents: {GetDirectoryContents(planFolder)}");
    }

    public static void AssertPlanState(string planFolder, string expectedState)
    {
        var yamlPath = Path.Combine(planFolder, "plan.yaml");
        Assert.True(File.Exists(yamlPath), $"plan.yaml not found at {yamlPath}");

        var content = File.ReadAllText(yamlPath);
        Assert.Contains($"state: {expectedState}", content,
            StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertPlanYamlContains(string planFolder, string expectedContent)
    {
        var yamlPath = Path.Combine(planFolder, "plan.yaml");
        var content = File.ReadAllText(yamlPath);
        Assert.Contains(expectedContent, content, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertRevisionExists(string planFolder, int minCount = 1)
    {
        var revisionsDir = Path.Combine(planFolder, "revisions");
        if (!Directory.Exists(revisionsDir))
        {
            Assert.Fail($"revisions/ directory not found at {revisionsDir}");
            return;
        }

        var files = Directory.GetFiles(revisionsDir, "*.md");
        Assert.True(files.Length >= minCount,
            $"Expected at least {minCount} revision(s), found {files.Length} in {revisionsDir}");
    }

    public static void AssertPlanFolderCreatedByAgent(string plansDir, string titleFragment)
    {
        var folder = FindPlanFolderByTitle(plansDir, titleFragment);
        Assert.NotNull(folder);
        AssertPlanYamlExists(folder!);
    }

    public static string? FindPlanFolderByTitle(string plansDir, string titleFragment)
    {
        if (!Directory.Exists(plansDir)) return null;

        var normalized = titleFragment.Replace(" ", "").Replace("-", "");
        foreach (var dir in Directory.GetDirectories(plansDir))
        {
            var folderName = Path.GetFileName(dir);
            var dashIdx = folderName.IndexOf('-');
            if (dashIdx < 0) continue;

            var namePart = folderName[(dashIdx + 1)..].Replace("-", "");
            if (namePart.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return dir;
        }

        return null;
    }

    public static void AssertBranchExists(string repoPath, string branchPattern)
    {
        var result = RunGit(repoPath, "branch --all");
        Assert.Contains(branchPattern, result,
            StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertCommitsOnBranch(string repoPath, string branch, string containsText)
    {
        var result = RunGit(repoPath, $"log {branch} --oneline -10");
        Assert.Contains(containsText, result, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertPromptwareLogWritten(string tendrilHome, string promptwareName)
    {
        var logsDir = Path.Combine(tendrilHome, "Promptwares", promptwareName, "Logs");
        if (!Directory.Exists(logsDir)) return; // Lenient — logs may not exist in dotnet run mode

        var logFiles = Directory.GetFiles(logsDir, "*.md");
        Assert.True(logFiles.Length > 0,
            $"No log files found in {logsDir}");
    }

    public static void AssertExitSuccess(PromptwareResult result, string promptwareName)
    {
        Assert.True(result.ExitCode == 0,
            $"Promptware '{promptwareName}' failed with exit code {result.ExitCode}.\n" +
            $"Duration: {result.Duration.TotalSeconds:F1}s\n" +
            $"Stdout (last 30 lines):\n{string.Join("\n", result.StdoutLines.TakeLast(30))}\n" +
            $"Stderr (last 30 lines):\n{string.Join("\n", result.StderrLines.TakeLast(30))}");
    }

    public static void AssertNoAgentErrors(PromptwareResult result)
    {
        var errorPatterns = new[]
        {
            "Agent binary not found",
            "authentication required",
            "API key",
            "rate limit",
            "ENOENT"
        };

        var allOutput = string.Join("\n", result.StderrLines);
        foreach (var pattern in errorPatterns)
        {
            Assert.DoesNotContain(pattern, allOutput, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string RunGit(string repoPath, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output;
    }

    private static string GetDirectoryContents(string path)
    {
        if (!Directory.Exists(path)) return "(directory does not exist)";
        var entries = Directory.GetFileSystemEntries(path).Select(Path.GetFileName);
        return string.Join(", ", entries);
    }
}
