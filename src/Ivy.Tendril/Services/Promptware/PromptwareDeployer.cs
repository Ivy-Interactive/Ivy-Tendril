using System.IO.Compression;

namespace Ivy.Tendril.Services.Promptware;

internal static class PromptwareDeployer
{
    /// <summary>
    ///     Embedded promptwares zip resource. Uses lowercase "promptwares" for historical reasons;
    ///     the directory structure was migrated from .promptwares/ to Promptwares/ in plan 02306.
    /// </summary>
    private const string ResourceName = "Ivy.Tendril.promptwares.zip";

    private const string VersionFileName = ".version";

    /// <summary>
    ///     Extracts embedded promptwares.zip to targetDir, preserving existing Memory/ and Tools/
    ///     directories, and ensuring both exist for every deployed promptware.
    /// </summary>
    public static void Deploy(string targetDir)
    {
        var assembly = typeof(PromptwareDeployer).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            throw new InvalidOperationException("Embedded promptwares.zip resource not found.");

        DeployFromZip(stream, targetDir);
    }

    /// <summary>
    ///     Core deploy logic: extracts <paramref name="zipStream" /> into targetDir, preserving existing
    ///     Memory/ and Tools/ directories and ensuring both exist for every deployed promptware.
    ///     Exposed as internal so tests exercise the real algorithm rather than a copy.
    /// </summary>
    internal static void DeployFromZip(Stream zipStream, string targetDir)
    {
        var tempDir = targetDir + "-deploying-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            // Extract to temp directory
            ZipFile.ExtractToDirectory(zipStream, tempDir);

            // Ensure target exists
            Directory.CreateDirectory(targetDir);

            // For each promptware subfolder, preserve Memory/ and Tools/
            foreach (var sourceSubDir in Directory.GetDirectories(tempDir))
            {
                var subDirName = Path.GetFileName(sourceSubDir);
                var targetSubDir = Path.Combine(targetDir, subDirName);

                // Move aside existing Memory/ and Tools/ if they exist.
                // Tools/ holds agent/user-authored tools (written via `tendril promptware write-tool`)
                // and must survive upgrades just like Memory/. No promptware currently *ships* Tools/
                // (all $shippedTools allowlists in pack-promptwares.ps1 are empty), so a straight
                // preserve is correct; if shipped tools are ever added this must become a merge
                // (overlay shipped files onto preserved runtime files) instead of a wholesale preserve.
                var preservedDirs = new List<(string original, string aside)>();
                foreach (var preserve in new[] { "Memory", "Tools" })
                {
                    var existingDir = Path.Combine(targetSubDir, preserve);
                    if (Directory.Exists(existingDir))
                    {
                        // Move preserved dirs into tempDir (not as subdirs of targetSubDir) so they aren't
                        // deleted when we recursively delete targetSubDir. tempDir is a sibling of targetDir
                        // and therefore on the SAME volume as targetSubDir — Directory.Move cannot move
                        // across volumes, so Path.GetTempPath() (often a different drive) must NOT be used.
                        var asideDir = Path.Combine(tempDir,
                            $".preserved-{subDirName}-{preserve}-" + Guid.NewGuid().ToString("N")[..8]);
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

                    // Guarantee every promptware has Memory/ and Tools/ (idempotent).
                    foreach (var folder in new[] { "Memory", "Tools" })
                        Directory.CreateDirectory(Path.Combine(targetSubDir, folder));
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
        if (deployed != GetCurrentVersion())
            return true;

        try
        {
            var assembly = typeof(PromptwareDeployer).Assembly;
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream != null)
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    var parts = entry.FullName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        var folderName = parts[0];
                        if (folderName != "memories" && folderName != "AgentChat" && folderName != "AGENTS.md" && folderName != ".DS_Store" && folderName != "logs" && folderName != "programs" && folderName != "config.json" && folderName != ".version" && folderName != ".gitignore")
                        {
                            var targetSubDir = Path.Combine(targetDir, folderName);
                            if (!Directory.Exists(targetSubDir))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback on error
        }

        return false;
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
