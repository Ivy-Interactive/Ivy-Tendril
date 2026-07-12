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
        if (Environment.CommandLine.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
            Environment.CommandLine.Contains("vstest", StringComparison.OrdinalIgnoreCase) ||
            Environment.CommandLine.Contains("xunit", StringComparison.OrdinalIgnoreCase))
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

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
        var excludedTemplatePath = Path.Combine(tendrilHome, "Promptwares");

        while (dir != null)
        {
            var vaultPath = Path.Combine(dir, "Promptwares");
            if (Directory.Exists(vaultPath) && !vaultPath.Equals(excludedTemplatePath, StringComparison.OrdinalIgnoreCase))
                return vaultPath;

            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public static string ResolveMemoryDirectory(string promptwareName, string? tendrilHome, string? planFolder = null)
    {
        // 1. Resolve local Promptwares vault memories first
        var vaultPath = ResolveBrainwaresVaultDir(planFolder);
        if (vaultPath != null)
        {
            var memoriesDir = Path.Combine(vaultPath, "memories");
            if (Directory.Exists(memoriesDir))
                return memoriesDir;
        }

        // 2. Fall back to user-wide global brainwares memories (~/.config/brainwares/memories)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalMemoriesDir = Path.Combine(userProfile, ".config", "brainwares", "memories");
        if (Directory.Exists(globalMemoriesDir))
            return globalMemoriesDir;

        // 3. Fall back to promptware folder's local Memory folder (development/packaging fallback)
        var promptwareFolder = ResolvePromptwareFolder(promptwareName, tendrilHome);
        var localProjMemory = Path.Combine(promptwareFolder, "Memory");
        if (Directory.Exists(localProjMemory))
            return localProjMemory;

        // Default to globalMemoriesDir so we always have a valid write target
        return globalMemoriesDir;
    }

    public static string GetBwPath()
    {
        var paths = new[]
        {
            "bw",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "bw"),
            "/usr/local/bin/bw",
            "/usr/bin/bw",
            "/opt/homebrew/bin/bw"
        };

        foreach (var p in paths)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = p,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(1000);
                    if (proc.ExitCode == 0) return p;
                }
            }
            catch { /* skip */ }
        }

        return "bw";
    }

    public static void EnsureGlobalBrainwaresConfig()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configDir = Path.Combine(home, ".config", "brainwares");
            var configPath = Path.Combine(configDir, "config.json");

            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var configJson = "{\n  \"default_vault_dir\": \"Promptwares\",\n  \"ignore_patterns\": [\n    \"node_modules\",\n    \"target\",\n    \"bin\",\n    \"obj\",\n    \".git\"\n  ]\n}";
            File.WriteAllText(configPath, configJson);
        }
        catch { /* best effort */ }
    }

    public static void EnsureLocalVault(string workspaceDir)
    {
        try
        {
            var vaultPath = Path.Combine(workspaceDir, "Promptwares");
            if (!Directory.Exists(vaultPath))
            {
                Directory.CreateDirectory(vaultPath);
                var bwPath = GetBwPath();
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = bwPath,
                    Arguments = "init",
                    WorkingDirectory = workspaceDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit();
            }
        }
        catch { /* best effort */ }
    }
}
