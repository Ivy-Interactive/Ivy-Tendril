namespace Ivy.Tendril.Helpers;

public static class PromptwareHelper
{
    public static string ResolvePromptwareFolder(string promptwareName, string? tendrilHome, string? promptwarePath = null)
    {
        if (!string.IsNullOrEmpty(promptwarePath))
        {
            var overrideFolder = Path.Combine(promptwarePath, promptwareName);
            if (File.Exists(Path.Combine(overrideFolder, "Program.md")) || Directory.Exists(Path.Combine(overrideFolder, "Memory")) || Directory.Exists(overrideFolder))
                return overrideFolder;
        }

        if (string.IsNullOrEmpty(tendrilHome))
            tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            var deployedRoot = Path.Combine(tendrilHome, "Promptwares");
            var deployedFolder = Path.Combine(deployedRoot, promptwareName);
            if (File.Exists(Path.Combine(deployedFolder, "Program.md")) || Directory.Exists(Path.Combine(deployedFolder, "Memory")) || Directory.Exists(deployedFolder))
                return deployedFolder;
        }

        var sourceRoot = ResolvePromptsRoot(tendrilHome);
        var sourceFolder = Path.Combine(sourceRoot, promptwareName);

        if (File.Exists(Path.Combine(sourceFolder, "Program.md")) || Directory.Exists(Path.Combine(sourceFolder, "Memory")) || Directory.Exists(sourceFolder))
            return sourceFolder;

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

    public static void RequireProgramFolder(string programFolder, string promptwareName, string? tendrilHome)
    {
        if (File.Exists(Path.Combine(programFolder, "Program.md")) || Directory.Exists(Path.Combine(programFolder, "Memory")) || Directory.Exists(programFolder))
            return;

        var promptsRoot = ResolvePromptsRoot(tendrilHome);
        var available = Directory.Exists(promptsRoot)
            ? string.Join(", ", Directory.EnumerateDirectories(promptsRoot)
                .Where(d => File.Exists(Path.Combine(d, "Program.md")) || Directory.Exists(Path.Combine(d, "Memory")))
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal))
            : "";
        if (string.IsNullOrEmpty(available))
            available = "(none)";

        throw new FileNotFoundException(
            $"Promptware not found: {promptwareName}. Available promptwares: {available}",
            programFolder);
    }

    public static string? ResolveBrainwaresVaultDir(string? workspaceDir = null)
    {
        workspaceDir ??= Directory.GetCurrentDirectory();
        var localVault = Path.Combine(workspaceDir, ".brainwares");
        if (Directory.Exists(localVault)) return localVault;

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
        var globalVault = Path.Combine(tendrilHome, "Promptwares");
        return Directory.Exists(globalVault) ? globalVault : null;
    }

    public static string? FindProjectNameForPath(string path, string tendrilHome)
    {
        return "global";
    }
}
