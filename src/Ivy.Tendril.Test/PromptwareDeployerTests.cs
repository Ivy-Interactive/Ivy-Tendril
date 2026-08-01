using System.IO.Compression;
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
    public void Deploy_ExtractsZipAndPreservesMemory()
    {
        // Arrange: Create target directory with existing Memory
        var targetDir = Path.Combine(_tempDir, "Promptwares");
        var promptwareADir = Path.Combine(targetDir, "PromptwareA");
        var memoryDir = Path.Combine(promptwareADir, "Memory");

        Directory.CreateDirectory(memoryDir);

        var existingMemory = Path.Combine(memoryDir, "test.md");
        File.WriteAllText(existingMemory, "# Existing Memory");

        // Create mock zip with new promptware content
        var mockZip = CreateMockZip();

        // Act: run the real deploy algorithm against the mock zip stream
        PromptwareDeployer.DeployFromZip(mockZip, targetDir);

        // Assert: New files were deployed
        var programFile = Path.Combine(promptwareADir, "Program.md");
        Assert.True(File.Exists(programFile), "Program.md should be deployed");
        Assert.Contains("# PromptwareA Program", File.ReadAllText(programFile));

        // Assert: Existing Memory was preserved
        Assert.True(File.Exists(existingMemory), "Existing memory file should be preserved");
        Assert.Equal("# Existing Memory", File.ReadAllText(existingMemory));
    }

    [Fact]
    public void Deploy_DoesNotCreateALogsDirectory()
    {
        // Job artifacts live in <TendrilHome>/Jobs/, never under a promptware.
        var targetDir = Path.Combine(_tempDir, "Promptwares");

        PromptwareDeployer.DeployFromZip(CreateMockZip(), targetDir);

        Assert.False(Directory.Exists(Path.Combine(targetDir, "PromptwareA", "Logs")));
    }

    [Fact]
    public void Deploy_PreservesRuntimeAuthoredTools()
    {
        // Arrange: target has a Tools/ file written at runtime (e.g. via `tendril promptware write-tool`).
        // The shipped zip carries no Tools/, so an upgrade must not delete it.
        var targetDir = Path.Combine(_tempDir, "Promptwares");
        var toolsDir = Path.Combine(targetDir, "PromptwareA", "Tools");
        Directory.CreateDirectory(toolsDir);
        var customTool = Path.Combine(toolsDir, "custom.md");
        File.WriteAllText(customTool, "# Custom Tool");

        // Act
        PromptwareDeployer.DeployFromZip(CreateMockZip(), targetDir);

        // Assert: runtime-authored tool survived the upgrade
        Assert.True(File.Exists(customTool), "Runtime-authored Tools/ file should be preserved");
        Assert.Equal("# Custom Tool", File.ReadAllText(customTool));
    }

    [Fact]
    public void Deploy_EnsuresMemoryAndToolsExist()
    {
        // Arrange: fresh install — target has no promptware folders yet.
        var targetDir = Path.Combine(_tempDir, "Promptwares");

        // Act
        PromptwareDeployer.DeployFromZip(CreateMockZip(), targetDir);

        // Assert: both runtime folders exist even though the zip ships neither
        //         (CreateMockZip mirrors the real stripped zip — Program.md only)
        var promptwareADir = Path.Combine(targetDir, "PromptwareA");
        foreach (var folder in new[] { "Memory", "Tools" })
            Assert.True(Directory.Exists(Path.Combine(promptwareADir, folder)),
                $"{folder}/ should exist after deploy");
    }

    // Mirrors the real shipped zip, which strips Memory/ and Tools/
    // (pack-promptwares.ps1): a promptware ships as Program.md only.
    private static MemoryStream CreateMockZip()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var programEntry = archive.CreateEntry("PromptwareA/Program.md");
            using var writer = new StreamWriter(programEntry.Open());
            writer.WriteLine("# PromptwareA Program");
            writer.WriteLine("This is the program content.");
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void CleanupOrphanedPreservedDirectories_RemovesPreservedDirectories()
    {
        // Arrange
        var promptwaresDir = Path.Combine(_tempDir, "Promptwares");
        var updatePlanDir = Path.Combine(promptwaresDir, "UpdatePlan");
        var orphanedDir = Path.Combine(updatePlanDir, "Logs-preserved-abc123");
        Directory.CreateDirectory(orphanedDir);
        File.WriteAllText(Path.Combine(orphanedDir, "test.md"), "orphaned content");

        // Act
        PromptwareDeployer.CleanupOrphanedPreservedDirectories(promptwaresDir);

        // Assert
        Assert.False(Directory.Exists(orphanedDir));
    }

    [Fact]
    public void CleanupOrphanedPreservedDirectories_IgnoresNormalDirectories()
    {
        // Arrange
        var promptwaresDir = Path.Combine(_tempDir, "Promptwares");
        var normalDir = Path.Combine(promptwaresDir, "UpdatePlan", "Logs");
        Directory.CreateDirectory(normalDir);
        File.WriteAllText(Path.Combine(normalDir, "test.md"), "normal content");

        // Act
        PromptwareDeployer.CleanupOrphanedPreservedDirectories(promptwaresDir);

        // Assert
        Assert.True(Directory.Exists(normalDir));
    }

    // Regression guard for #1551: concurrent CreatePr jobs share $TMPDIR, so a fixed
    // body-file name like pr-body.md is a last-writer-wins race that swaps PR bodies
    // between plans. The fix requires a unique temp file per invocation (mktemp).
    [Fact]
    public void CreatePrProgram_UsesUniqueTempFileForPrBody()
    {
        var sourcePromptwarePath = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "Ivy.Tendril", "Promptwares"));
        var programFile = Path.Combine(sourcePromptwarePath, "CreatePr", "Program.md");

        Assert.True(File.Exists(programFile), $"Expected to find {programFile}");
        var content = File.ReadAllText(programFile);

        Assert.Contains("mktemp", content);
        // The fixed literal is still mentioned in prose as a "don't do this" example,
        // so guard against the actual broken usage rather than the bare substring.
        Assert.DoesNotContain("> \"$TMPDIR/pr-body.md\"", content);
        Assert.DoesNotContain("--body-file \"$TMPDIR/pr-body.md\"", content);
    }

    // Regression guard for plan 00108: CreatePr was unconditionally passing --admin and
    // merging immediately, bypassing branch protection and racing the CI checks. The fix
    // adds a "gh pr checks" gate that waits for and validates CI results before merging,
    // and removes the unconditional --admin (it may only be used as a retry after the
    // check gate has passed). Enforces this rule across both copies of Program.md.
    [Fact]
    public void CreatePrProgram_GatesMergeOnChecksAndDoesNotAlwaysPassAdmin()
    {
        var sourcePromptwarePath = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "Ivy.Tendril", "Promptwares"));
        var programFile1 = Path.Combine(sourcePromptwarePath, "CreatePr", "Program.md");

        var sourceTeamIvyPath = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "Ivy.Tendril.TeamIvyConfig", "Promptwares"));
        var programFile2 = Path.Combine(sourceTeamIvyPath, "CreatePr", "Program.md");

        Assert.True(File.Exists(programFile1), $"Expected to find {programFile1}");
        Assert.True(File.Exists(programFile2), $"Expected to find {programFile2}");

        var content1 = File.ReadAllText(programFile1);
        var content2 = File.ReadAllText(programFile2);

        // Verify the check gate is present and reads buckets
        Assert.Contains("gh pr checks", content1);
        Assert.Contains("--json bucket", content1);
        Assert.Contains("gh pr checks", content2);
        Assert.Contains("--json bucket", content2);

        // Verify the unconditional --admin instructions are removed
        Assert.DoesNotContain("Always pass `--merge --admin`", content1);
        Assert.DoesNotContain("always `--merge --admin`", content2);

        // Verify --required is not used (measured vacuous on unprotected repos)
        Assert.DoesNotContain("--required", content1);
        Assert.DoesNotContain("--required", content2);
    }
}
