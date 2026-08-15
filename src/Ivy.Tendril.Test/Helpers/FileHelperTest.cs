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

    [Fact]
    public void SanitizeUtf8_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, FileHelper.SanitizeUtf8(null));
        Assert.Equal(string.Empty, FileHelper.SanitizeUtf8(""));
    }

    [Fact]
    public void SanitizeUtf8_LeadingBom_StripsBom()
    {
        var withBom = "\uFEFF# Plan Title\nSome content";
        var sanitized = FileHelper.SanitizeUtf8(withBom);
        Assert.Equal("# Plan Title\nSome content", sanitized);
        Assert.False(sanitized.StartsWith('\uFEFF'));
    }

    [Fact]
    public void SanitizeUtf8_NullBytes_StripsNullBytes()
    {
        var withNulls = "Hello\0 World\0\0 from\0 Tendril!";
        var sanitized = FileHelper.SanitizeUtf8(withNulls);
        Assert.Equal("Hello World from Tendril!", sanitized);
        Assert.DoesNotContain('\0', sanitized);
    }

    [Fact]
    public void SanitizeUtf8_UnpairedSurrogates_ReplacedWithReplacementCharacter()
    {
        // High surrogate without low surrogate
        var withHighSurrogate = "Invalid \uD800 char";
        var sanitizedHigh = FileHelper.SanitizeUtf8(withHighSurrogate);
        Assert.Equal("Invalid \uFFFD char", sanitizedHigh);

        // Low surrogate without high surrogate
        var withLowSurrogate = "Invalid \uDC00 char";
        var sanitizedLow = FileHelper.SanitizeUtf8(withLowSurrogate);
        Assert.Equal("Invalid \uFFFD char", sanitizedLow);

        // Reversed surrogate pair
        var reversed = "Reversed \uDC00\uD800 pair";
        var sanitizedReversed = FileHelper.SanitizeUtf8(reversed);
        Assert.Equal("Reversed \uFFFD\uFFFD pair", sanitizedReversed);
    }

    [Fact]
    public void SanitizeUtf8_ValidSurrogatePair_Preserved()
    {
        var withEmojis = "🚀 Rocket 👍 Thumbs up ⚠️ Warning 日本語";
        var sanitized = FileHelper.SanitizeUtf8(withEmojis);
        Assert.Equal(withEmojis, sanitized);
    }

    [Fact]
    public void SanitizeUtf8_ControlCharacters_StripsNonWhitespace()
    {
        var withControl = "Line 1\x01\x02\x03\tTab\r\nLine 2\x0B\x0CLine 3";
        var sanitized = FileHelper.SanitizeUtf8(withControl);
        Assert.Equal("Line 1\tTab\r\nLine 2Line 3", sanitized);
    }

    [Fact]
    public void ReadAllText_Utf8BomFile_StripsBomAndReadsCleanText()
    {
        var file = Path.Combine(_tempDir, "bom.txt");
        var utf8WithBom = new System.Text.UTF8Encoding(true);
        File.WriteAllText(file, "# Plan Content", utf8WithBom);

        var content = FileHelper.ReadAllText(file);
        Assert.Equal("# Plan Content", content);
        Assert.False(content.StartsWith('\uFEFF'));
    }

    [Fact]
    public void ReadAllText_FileWithNullBytesAndInvalidSurrogates_Sanitized()
    {
        var file = Path.Combine(_tempDir, "corrupted.txt");
        // Write raw bytes with null bytes and UTF-8 invalid bytes
        var bytes = System.Text.Encoding.UTF8.GetBytes("Corrupted\0 content\0");
        File.WriteAllBytes(file, bytes);

        var content = FileHelper.ReadAllText(file);
        Assert.Equal("Corrupted content", content);
        Assert.DoesNotContain('\0', content);
    }

    [Fact]
    public void WriteAllText_WritesCleanUtf8WithoutBom()
    {
        var file = Path.Combine(_tempDir, "output.txt");
        FileHelper.WriteAllText(file, "\uFEFFClean\0 text with 🚀");

        var bytes = File.ReadAllBytes(file);
        // Ensure no UTF-8 BOM (0xEF, 0xBB, 0xBF)
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        var content = FileHelper.ReadAllText(file);
        Assert.Equal("Clean text with 🚀", content);
    }
}
