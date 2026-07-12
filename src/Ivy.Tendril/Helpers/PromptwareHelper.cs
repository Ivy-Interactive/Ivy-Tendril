namespace Ivy.Tendril.Helpers;

public static class PromptwareHelper
{
    public static string ResolvePromptwareFolder(string promptwareName, string? tendrilHome, string? promptwarePath = null)
    {
        if (!string.IsNullOrEmpty(promptwarePath))
        {
            var overrideFolder = Path.Combine(promptwarePath, promptwareName);
            if (File.Exists(Path.Combine(overrideFolder, "Program.md")))
                return overrideFolder;
        }

        var sourceRoot = ResolvePromptsRoot(tendrilHome);
        var sourceFolder = Path.Combine(sourceRoot, promptwareName);

        if (File.Exists(Path.Combine(sourceFolder, "Program.md")))
            return sourceFolder;

        if (string.IsNullOrEmpty(tendrilHome))
            tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            var deployedRoot = Path.Combine(tendrilHome, "Promptwares");
            var deployedFolder = Path.Combine(deployedRoot, promptwareName);
            if (File.Exists(Path.Combine(deployedFolder, "Program.md")))
                return deployedFolder;
        }

        return sourceFolder;
    }

    public static string ResolvePromptsRoot(string? tendrilHome = null)
    {
        var sourceRoot = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "Promptwares"));

        if (Directory.Exists(sourceRoot))
            return sourceRoot;

        if (string.IsNullOrEmpty(tendrilHome))
            tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            var deployedRoot = Path.Combine(tendrilHome, "Promptwares");
            if (Directory.Exists(deployedRoot))
                return deployedRoot;
        }

        if (!string.IsNullOrEmpty(tendrilHome))
            return Path.Combine(tendrilHome, "Promptwares");

        return sourceRoot;
    }

    public static string? ResolveBrainwaresVaultDir(string? startDirectory = null)
    {
        // Prevent unit/integration tests from detecting the real vault
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => 
            a.FullName!.Contains("Test", StringComparison.OrdinalIgnoreCase) || 
            a.FullName!.Contains("xunit", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var dir = startDirectory ?? Directory.GetCurrentDirectory();
        try
        {
            dir = Path.GetFullPath(dir);
        }
        catch
        {
            return null;
        }

        while (dir != null)
        {
            var vaultPath = Path.Combine(dir, ".brainwares");
            if (Directory.Exists(vaultPath))
                return vaultPath;

            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public static string ResolveMemoryDirectory(string promptwareName, string? tendrilHome, string? planFolder = null)
    {
        var vaultPath = ResolveBrainwaresVaultDir(planFolder);
        if (vaultPath != null)
        {
            var memoriesDir = Path.Combine(vaultPath, "memories");
            if (Directory.Exists(memoriesDir))
                return memoriesDir;
        }

        var promptwareFolder = ResolvePromptwareFolder(promptwareName, tendrilHome);
        return Path.Combine(promptwareFolder, "Memory");
    }
}
