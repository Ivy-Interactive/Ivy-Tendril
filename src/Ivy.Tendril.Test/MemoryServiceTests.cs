using System;
using System.IO;
using System.Linq;
using Ivy.Tendril.Services.Memory;
using Xunit;

namespace Ivy.Tendril.Test;

public class MemoryServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly MemoryService _memoryService;

    public MemoryServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TendrilMemoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        Environment.SetEnvironmentVariable("TENDRIL_HOME", Path.Combine(_testDir, ".tendril"));
        _memoryService = new MemoryService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void AddReadWriteDeleteMemory_WorksCorrectly()
    {
        var noteName = "test-note";
        var title = "Test Memory Note";
        var tags = new[] { "test", "unit" };

        var note = _memoryService.AddMemory(noteName, title, tags, "# Content", _testDir, "testproject");
        Assert.NotNull(note);
        Assert.Equal("test-note", note.Name);
        Assert.Equal(title, note.Title);

        var readNote = _memoryService.ReadMemory(noteName, _testDir, "testproject");
        Assert.NotNull(readNote);
        Assert.Contains("# Content", readNote!.Content);

        _memoryService.WriteMemory(noteName, "# Updated Content", _testDir, "testproject");
        var updatedNote = _memoryService.ReadMemory(noteName, _testDir, "testproject");
        Assert.NotNull(updatedNote);
        Assert.Contains("# Updated Content", updatedNote!.Content);

        _memoryService.DeleteMemory(noteName, _testDir, "testproject");
        var deletedNote = _memoryService.ReadMemory(noteName, _testDir, "testproject");
        Assert.Null(deletedNote);
    }

    [Fact]
    public void LinkFile_AndUpdate_TracksHashChangesCorrectly()
    {
        var noteName = "code-note";
        var relFilePath = "src/TestFile.cs";
        var fullFilePath = Path.Combine(_testDir, relFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullFilePath)!);
        File.WriteAllText(fullFilePath, "public class TestFile {}");

        _memoryService.AddMemory(noteName, workspaceDir: _testDir, projectName: "testproject");
        _memoryService.LinkFile(noteName, relFilePath, workspaceDir: _testDir, projectName: "testproject");

        var statusClean = _memoryService.GetStatus(_testDir, "testproject");
        Assert.Equal(0, statusClean.OutdatedMemories);

        // Modify file content to trigger hash mismatch
        File.WriteAllText(fullFilePath, "public class TestFile { public int Modified { get; set; } }");
        var statusOutdated = _memoryService.GetStatus(_testDir, "testproject");
        Assert.Equal(1, statusOutdated.OutdatedMemories);
        Assert.Contains(noteName, statusOutdated.OutdatedNoteNames);

        // Update memory hash
        _memoryService.UpdateMemory(noteName, workspaceDir: _testDir, projectName: "testproject");
        var statusSynced = _memoryService.GetStatus(_testDir, "testproject");
        Assert.Equal(0, statusSynced.OutdatedMemories);
    }

    [Fact]
    public void MissingFile_AndBrokenLink_DetectedInStatus()
    {
        var noteName = "broken-note";
        var relFilePath = "src/DeletedFile.cs";
        var fullFilePath = Path.Combine(_testDir, relFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullFilePath)!);
        File.WriteAllText(fullFilePath, "temp content");

        _memoryService.AddMemory(noteName, workspaceDir: _testDir, projectName: "testproject");
        _memoryService.LinkFile(noteName, relFilePath, workspaceDir: _testDir, projectName: "testproject");
        _memoryService.RelateMemories(noteName, "non-existent-target", workspaceDir: _testDir, projectName: "testproject");

        // Delete the code file
        File.Delete(fullFilePath);

        var status = _memoryService.GetStatus(_testDir, "testproject");
        Assert.True(status.OutdatedMemories > 0);
        Assert.True(status.BrokenLinks > 0);
        Assert.Contains("[MISSING CODE]", status.RawOutput);
        Assert.Contains("[BROKEN LINK]", status.RawOutput);
    }

    [Fact]
    public void QueryMemories_AndGetRulesMarkdown_WorksCorrectly()
    {
        _memoryService.AddMemory("arch-overview", "Architecture Overview", new[] { "arch" }, "System is modular.", _testDir, "testproject");
        _memoryService.AddMemory("database-schema", "Database Schema", new[] { "db" }, "SQLite DB used.", _testDir, "testproject");

        var results = _memoryService.QueryMemories("modular", _testDir, "testproject").ToList();
        Assert.Single(results);
        Assert.Equal("arch-overview", results[0].Name);

        var rules = _memoryService.GetRulesMarkdown(_testDir, "testproject");
        Assert.Contains("Codebase Memories & Vault Context", rules);
        Assert.Contains("Architecture Overview", rules);
        Assert.Contains("Database Schema", rules);
    }
}
