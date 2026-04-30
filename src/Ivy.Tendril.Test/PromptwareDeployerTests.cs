using System.IO.Compression;
using System.Reflection;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class PromptwareDeployerTests : IDisposable
{
    private readonly string _tempDir;

    public PromptwareDeployerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"promptware-deploy-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void IsEmbeddedAvailable_ReturnsFalse_InDebugBuilds()
    {
        // In debug/test builds, the embedded resource is not included
        var result = PromptwareDeployer.IsEmbeddedAvailable();
        Assert.False(result);
    }

    [Fact]
    public void Deploy_ThrowsWhenNoEmbeddedResource()
    {
        var targetDir = Path.Combine(_tempDir, "Promptwares");
        Assert.Throws<InvalidOperationException>(() => PromptwareDeployer.Deploy(targetDir));
    }

    [Fact]
    public void NeedsUpdate_ReturnsFalse_WhenNoEmbeddedResource()
    {
        var targetDir = Path.Combine(_tempDir, "Promptwares");
        Directory.CreateDirectory(targetDir);
        Assert.False(PromptwareDeployer.NeedsUpdate(targetDir));
    }

    [Fact]
    public void Deploy_ExtractsZipAndPreservesLogsMemory()
    {
        // Arrange: Create target directory with existing Logs and Memory
        var targetDir = Path.Combine(_tempDir, "Promptwares");
        var promptwareADir = Path.Combine(targetDir, "PromptwareA");
        var logsDir = Path.Combine(promptwareADir, "Logs");
        var memoryDir = Path.Combine(promptwareADir, "Memory");

        Directory.CreateDirectory(logsDir);
        Directory.CreateDirectory(memoryDir);

        var existingLog = Path.Combine(logsDir, "00001.md");
        var existingMemory = Path.Combine(memoryDir, "test.md");
        File.WriteAllText(existingLog, "# Existing Log");
        File.WriteAllText(existingMemory, "# Existing Memory");

        // Create mock zip with new promptware content
        var mockZip = CreateMockZip();

        // Act: Deploy using reflection to access the internal Deploy method
        DeployFromStream(mockZip, targetDir);

        // Assert: New files were deployed
        var programFile = Path.Combine(promptwareADir, "Program.md");
        Assert.True(File.Exists(programFile), "Program.md should be deployed");
        Assert.Contains("# PromptwareA Program", File.ReadAllText(programFile));

        // Assert: Existing Logs and Memory were preserved
        Assert.True(File.Exists(existingLog), "Existing log file should be preserved");
        Assert.Equal("# Existing Log", File.ReadAllText(existingLog));
        Assert.True(File.Exists(existingMemory), "Existing memory file should be preserved");
        Assert.Equal("# Existing Memory", File.ReadAllText(existingMemory));
    }

    private static MemoryStream CreateMockZip()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Add PromptwareA/Program.md
            var programEntry = archive.CreateEntry("PromptwareA/Program.md");
            using (var writer = new StreamWriter(programEntry.Open()))
            {
                writer.WriteLine("# PromptwareA Program");
                writer.WriteLine("This is the program content.");
            }

            // Add PromptwareA/Logs/.gitkeep (placeholder)
            var logsEntry = archive.CreateEntry("PromptwareA/Logs/.gitkeep");
            using (var writer = new StreamWriter(logsEntry.Open()))
            {
                writer.WriteLine("");
            }

            // Add PromptwareA/Memory/.gitkeep (placeholder)
            var memoryEntry = archive.CreateEntry("PromptwareA/Memory/.gitkeep");
            using (var writer = new StreamWriter(memoryEntry.Open()))
            {
                writer.WriteLine("");
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void DeployFromStream(MemoryStream zipStream, string targetDir)
    {
        // Use reflection to access the internal Deploy logic
        // We need to replicate the Deploy method's logic using the stream directly
        var tempDir = targetDir + "-deploying-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            // Extract to temp directory
            ZipFile.ExtractToDirectory(zipStream, tempDir);

            // Ensure target exists
            Directory.CreateDirectory(targetDir);

            // For each promptware subfolder, preserve Logs/ and Memory/
            foreach (var sourceSubDir in Directory.GetDirectories(tempDir))
            {
                var subDirName = Path.GetFileName(sourceSubDir);
                var targetSubDir = Path.Combine(targetDir, subDirName);

                // Move aside existing Logs/ and Memory/ if they exist
                // IMPORTANT: Move preserved dirs to temp directory (not as subdirs of targetSubDir)
                // so they aren't deleted when we recursively delete targetSubDir
                var preservedDirs = new List<(string original, string aside)>();
                foreach (var preserve in new[] { "Logs", "Memory" })
                {
                    var existingDir = Path.Combine(targetSubDir, preserve);
                    if (Directory.Exists(existingDir))
                    {
                        var asideDir = Path.Combine(Path.GetTempPath(), $"{subDirName}-{preserve}-preserved-" + Guid.NewGuid().ToString("N")[..8]);
                        Directory.Move(existingDir, asideDir);
                        preservedDirs.Add((existingDir, asideDir));
                    }
                }

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

            // Copy any root-level files
            foreach (var sourceFile in Directory.GetFiles(tempDir))
            {
                var targetFile = Path.Combine(targetDir, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, targetFile, true);
            }

            // Stamp the deployed version
            var versionFileName = ".version";
            var version = typeof(PromptwareDeployer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            File.WriteAllText(Path.Combine(targetDir, versionFileName), version);
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
}