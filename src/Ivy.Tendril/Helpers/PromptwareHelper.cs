namespace Ivy.Tendril.Helpers;

public static class PromptwareHelper
{
    public static string ResolvePromptsRoot(string? tendrilHome = null)
    {
        // 1. Debug/source mode: check if Promptwares exists relative to BaseDirectory
        var sourceRoot = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "Promptwares"));
        if (Directory.Exists(sourceRoot))
            return sourceRoot;

        // 2. Production mode: use TENDRIL_HOME/Promptwares
        tendrilHome ??= Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            var deployedRoot = Path.Combine(tendrilHome, "Promptwares");
            if (Directory.Exists(deployedRoot))
                return deployedRoot;
        }

        // 3. Fallback (will fail at runtime, but gives a clear error location)
        if (!string.IsNullOrEmpty(tendrilHome))
            return Path.Combine(tendrilHome, "Promptwares");
            
        return sourceRoot;
    }
}
