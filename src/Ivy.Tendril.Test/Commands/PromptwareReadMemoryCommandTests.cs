using Ivy.Tendril.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace Ivy.Tendril.Test.Commands;

public class PromptwareReadMemoryCommandTests : IDisposable
{
    private readonly string _tempHome;

    public PromptwareReadMemoryCommandTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), "tendril-test-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempHome);
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", null);
        if (Directory.Exists(_tempHome))
        {
            try { Directory.Delete(_tempHome, true); } catch { }
        }
    }

    [Fact]
    public void Validation_Fails_When_Name_Or_Filenames_Empty()
    {
        var emptyNameSettings = new PromptwareReadMemorySettings { Name = "", Filenames = ["test.md"] };
        Assert.False(emptyNameSettings.Validate().Successful);

        var emptyFilenamesSettings = new PromptwareReadMemorySettings { Name = "SetupProject", Filenames = [] };
        Assert.False(emptyFilenamesSettings.Validate().Successful);
    }

    [Fact]
    public void Execute_SingleFile_OutputsContentWithoutHeader()
    {
        var memoryDir = Path.Combine(_tempHome, "Promptwares", "TestPromptware", "Memory");
        Directory.CreateDirectory(memoryDir);
        File.WriteAllText(Path.Combine(memoryDir, "notes.md"), "Sample memory content");

        using var sw = new StringWriter();
        var settings = new PromptwareReadMemorySettings { Name = "TestPromptware", Filenames = ["notes.md"] };
        var exitCode = PromptwareReadMemoryCommand.ExecuteInternal(settings, sw);

        Assert.Equal(0, exitCode);
        Assert.Equal("Sample memory content", sw.ToString());
    }

    [Fact]
    public void Execute_MultipleFiles_OutputsBatchedContentWithHeaders()
    {
        var memoryDir = Path.Combine(_tempHome, "Promptwares", "TestPromptware", "Memory");
        Directory.CreateDirectory(memoryDir);
        File.WriteAllText(Path.Combine(memoryDir, "notes.md"), "Sample memory content");
        File.WriteAllText(Path.Combine(memoryDir, "quirks.md"), "Quirks list content");

        using var sw = new StringWriter();
        var settings = new PromptwareReadMemorySettings { Name = "TestPromptware", Filenames = ["notes.md", "quirks.md"] };
        var exitCode = PromptwareReadMemoryCommand.ExecuteInternal(settings, sw);

        Assert.Equal(0, exitCode);
        var output = sw.ToString();
        Assert.Contains("=== notes.md ===", output);
        Assert.Contains("Sample memory content", output);
        Assert.Contains("=== quirks.md ===", output);
        Assert.Contains("Quirks list content", output);
    }

    [Fact]
    public void Execute_MissingFile_ThrowsFileNotFoundException()
    {
        var memoryDir = Path.Combine(_tempHome, "Promptwares", "TestPromptware", "Memory");
        Directory.CreateDirectory(memoryDir);

        var settings = new PromptwareReadMemorySettings { Name = "TestPromptware", Filenames = ["nonexistent.md"] };

        var ex = Assert.Throws<FileNotFoundException>(() => PromptwareReadMemoryCommand.ExecuteInternal(settings));
        Assert.Contains("nonexistent.md", ex.Message);
    }
}
