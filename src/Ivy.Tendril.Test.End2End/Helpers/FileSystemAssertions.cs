namespace Ivy.Tendril.Test.End2End.Helpers;

public static class FileSystemAssertions
{
    public static void AssertOnboardingComplete(string tendrilHome)
    {
        Assert.True(Directory.Exists(tendrilHome), $"TENDRIL_HOME not found: {tendrilHome}");
        Assert.True(Directory.Exists(Path.Combine(tendrilHome, "Plans")), "Plans/ directory missing");
        Assert.True(Directory.Exists(Path.Combine(tendrilHome, "Inbox")), "Inbox/ directory missing");
        Assert.True(Directory.Exists(Path.Combine(tendrilHome, "Trash")), "Trash/ directory missing");
        Assert.True(Directory.Exists(Path.Combine(tendrilHome, "Promptwares")), "Promptwares/ directory missing");
        Assert.True(Directory.Exists(Path.Combine(tendrilHome, "Hooks")), "Hooks/ directory missing");
        Assert.True(File.Exists(Path.Combine(tendrilHome, "config.yaml")), "config.yaml missing");
    }

    public static string? FindPlanFolder(string plansDir, string titleFragment)
    {
        if (!Directory.Exists(plansDir))
            return null;

        // Normalize by removing hyphens for comparison since folder names
        // may use CamelCase (e.g., "CreateClaudeLifecycleTxtFile")
        // while the search term may use hyphens (e.g., "claude-lifecycle")
        var normalizedFragment = titleFragment.Replace("-", "");

        return Directory.GetDirectories(plansDir)
            .FirstOrDefault(d =>
            {
                var name = Path.GetFileName(d);
                return name.Contains(titleFragment, StringComparison.OrdinalIgnoreCase) ||
                       name.Replace("-", "").Contains(normalizedFragment, StringComparison.OrdinalIgnoreCase);
            });
    }

    public static void AssertPlanExists(string plansDir, string titleFragment)
    {
        var folder = FindPlanFolder(plansDir, titleFragment);
        Assert.NotNull(folder);
        Assert.True(File.Exists(Path.Combine(folder!, "plan.yaml")),
            $"plan.yaml missing in {folder}");
    }

    public static void AssertPlanYamlState(string planFolder, string expectedState)
    {
        var yamlPath = Path.Combine(planFolder, "plan.yaml");
        Assert.True(File.Exists(yamlPath), $"plan.yaml not found: {yamlPath}");

        var content = File.ReadAllText(yamlPath);
        Assert.Contains($"state: {expectedState}", content, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertPlanHasRevision(string planFolder, int revisionNumber)
    {
        var revisionsDir = Path.Combine(planFolder, "revisions");
        Assert.True(Directory.Exists(revisionsDir), $"revisions/ directory missing in {planFolder}");

        var revisionFile = Path.Combine(revisionsDir, $"{revisionNumber:D3}.md");
        Assert.True(File.Exists(revisionFile), $"Revision file missing: {revisionFile}");
    }

    public static string? GetPlanId(string planFolderPath)
    {
        var folderName = Path.GetFileName(planFolderPath);
        var dashIndex = folderName.IndexOf('-');
        return dashIndex > 0 ? folderName[..dashIndex] : folderName;
    }

    public static void AssertConfigContains(string tendrilHome, string expectedText)
    {
        var configPath = Path.Combine(tendrilHome, "config.yaml");
        Assert.True(File.Exists(configPath), "config.yaml not found");

        var content = File.ReadAllText(configPath);
        Assert.Contains(expectedText, content, StringComparison.OrdinalIgnoreCase);
    }
}
