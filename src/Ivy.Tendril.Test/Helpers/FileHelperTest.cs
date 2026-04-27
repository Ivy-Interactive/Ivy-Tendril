using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class FileHelperTest : IDisposable
{
    private readonly string _tempDir;

    public FileHelperTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FileHelperTest_{Guid.NewGuid()}");
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
            // Best-effort cleanup
        }
    }

    [Fact]
    public void ReadAllText_SymbolicLink_ThrowsUnauthorizedAccessException()
    {
        var targetFile = Path.Combine(_tempDir, "target.txt");
        var linkFile = Path.Combine(_tempDir, "link.txt");
        File.WriteAllText(targetFile, "test content");

        try
        {
            File.CreateSymbolicLink(linkFile, targetFile);
        }
        catch (IOException)
        {
            // Skip test if we can't create symlinks (requires admin on Windows)
            return;
        }

        var ex = Assert.Throws<UnauthorizedAccessException>(() => FileHelper.ReadAllText(linkFile));
        Assert.Contains("symbolic link", ex.Message);
    }

    [Fact]
    public void WriteAllText_SymbolicLink_ThrowsUnauthorizedAccessException()
    {
        var targetFile = Path.Combine(_tempDir, "target.txt");
        var linkFile = Path.Combine(_tempDir, "link.txt");
        File.WriteAllText(targetFile, "original content");

        try
        {
            File.CreateSymbolicLink(linkFile, targetFile);
        }
        catch (IOException)
        {
            // Skip test if we can't create symlinks
            return;
        }

        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            FileHelper.WriteAllText(linkFile, "new content"));
        Assert.Contains("symbolic link", ex.Message);
    }

    [Fact]
    public void ReadAllText_RelativePath_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => FileHelper.ReadAllText("relative/path.txt"));
        Assert.Contains("fully qualified", ex.Message);
    }

    [Fact]
    public void WriteAllText_RelativePath_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            FileHelper.WriteAllText("relative/path.txt", "content"));
        Assert.Contains("fully qualified", ex.Message);
    }

    [Fact]
    public void EnumerateLines_SymbolicLink_ThrowsUnauthorizedAccessException()
    {
        var targetFile = Path.Combine(_tempDir, "target.txt");
        var linkFile = Path.Combine(_tempDir, "link.txt");
        File.WriteAllText(targetFile, "line1\nline2");

        try
        {
            File.CreateSymbolicLink(linkFile, targetFile);
        }
        catch (IOException)
        {
            // Skip test if we can't create symlinks
            return;
        }

        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
        {
            // Force enumeration
            var _ = FileHelper.EnumerateLines(linkFile).ToList();
        });
        Assert.Contains("symbolic link", ex.Message);
    }

    [Fact]
    public void ReadAllText_ValidPath_Succeeds()
    {
        var file = Path.Combine(_tempDir, "valid.txt");
        File.WriteAllText(file, "test content");

        var content = FileHelper.ReadAllText(file);
        Assert.Equal("test content", content);
    }

    [Fact]
    public void WriteAllText_ValidPath_Succeeds()
    {
        var file = Path.Combine(_tempDir, "valid.txt");

        FileHelper.WriteAllText(file, "test content");

        Assert.True(File.Exists(file));
        Assert.Equal("test content", File.ReadAllText(file));
    }
}
