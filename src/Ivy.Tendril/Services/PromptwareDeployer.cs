using System.IO.Compression;

namespace Ivy.Tendril.Services;

internal static class PromptwareDeployer
{
    /// <summary>
    ///     Embedded promptwares zip resource. Uses lowercase "promptwares" for historical reasons;
    ///     the directory structure was migrated from .promptwares/ to Promptwares/ in plan 02306.
    /// </summary>
    private const string ResourceName = "Ivy.Tendril.promptwares.zip";

    private const string VersionFileName = ".version";

    /// <summary>
    ///     Extracts embedded promptwares.zip to targetDir, preserving existing Logs/ and Memory/ directories.
    /// </summary>
    public static void Deploy(string targetDir)
    {
        var assembly = typeof(PromptwareDeployer).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            throw new InvalidOperationException("Embedded promptwares.zip resource not found.");

        var tempDir = targetDir + "-deploying-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            // Extract to temp directory
            ZipFile.ExtractToDirectory(stream, tempDir);

            // Ensure target exists
            Directory.CreateDirectory(targetDir);

            // For each promptware subfolder, preserve Logs/ and Memory/
            foreach (var sourceSubDir in Directory.GetDirectories(tempDir))
            {
                var subDirName = Path.GetFileName(sourceSubDir);
                var targetSubDir = Path.Combine(targetDir, subDirName);

                // Move aside existing Logs/ and Memory/ if they exist
                var preservedDirs = new List<(string original, string aside)>();
                foreach (var preserve in new[] { "Logs", "Memory" })
                {
                    var existingDir = Path.Combine(targetSubDir, preserve);
                    if (Directory.Exists(existingDir))
                    {
                        var asideDir = existingDir + "-preserved-" + Guid.NewGuid().ToString("N")[..8];
                        Directory.Move(existingDir, asideDir);
                        preservedDirs.Add((existingDir, asideDir));
                    }
                }

                try
                {
                    // Delete old promptware files (if target exists)
                    if (Directory.Exists(targetSubDir))
                        Directory.Delete(targetSubDir, true);

                    // Move new files from temp
                    Directory.Move(sourceSubDir, targetSubDir);

                    // Restore preserved directories
                    foreach (var (original, aside) in preservedDirs)
                    {
                        // Remove empty placeholder if it was created by the zip
                        if (Directory.Exists(original))
                            Directory.Delete(original, true);

                        Directory.Move(aside, original);
                    }
                }
                catch
                {
                    // If deployment fails after preservation, clean up preserved dirs
                    foreach (var (_, aside) in preservedDirs)
                    {
                        if (Directory.Exists(aside))
                        {
                            try { Directory.Delete(aside, true); }
                            catch { /* Best effort */ }
                        }
                    }
                    throw;
                }
            }

            // Copy any root-level files
            foreach (var sourceFile in Directory.GetFiles(tempDir))
            {
                var targetFile = Path.Combine(targetDir, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, targetFile, true);
            }

            // Stamp the deployed version
            File.WriteAllText(Path.Combine(targetDir, VersionFileName), GetCurrentVersion());
        }
        finally
        {
            // Clean up temp directory
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch { /* Best effort */ }
            }
        }
    }

    /// <summary>
    ///     Removes orphaned *-preserved-* directories from previous failed deployments.
    /// </summary>
    public static void CleanupOrphanedPreservedDirectories(string targetDir)
    {
        if (!Directory.Exists(targetDir))
            return;

        foreach (var subDir in Directory.GetDirectories(targetDir))
        {
            // Scan each promptware subfolder for preserved directories
            foreach (var dir in Directory.GetDirectories(subDir))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.Contains("-preserved-"))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // Best effort — log but don't block startup
                    }
                }
            }
        }
    }

    public static bool NeedsUpdate(string targetDir)
    {
        if (!IsEmbeddedAvailable())
            return false;

        var versionFile = Path.Combine(targetDir, VersionFileName);
        if (!File.Exists(versionFile))
            return true;

        var deployed = File.ReadAllText(versionFile).Trim();
        return deployed != GetCurrentVersion();
    }

    public static bool IsEmbeddedAvailable()
    {
        var assembly = typeof(PromptwareDeployer).Assembly;
        return assembly.GetManifestResourceNames().Contains(ResourceName);
    }

    private static string GetCurrentVersion()
    {
        return typeof(PromptwareDeployer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
