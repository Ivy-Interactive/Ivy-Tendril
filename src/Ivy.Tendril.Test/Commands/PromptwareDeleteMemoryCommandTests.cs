using Ivy.Tendril.Commands;
using Ivy.Tendril.Test;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test.Commands;

[Collection("TendrilHome")]
public class PromptwareDeleteMemoryCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-promptware-delete-test");
    private readonly string _originalTendrilHome;

    public PromptwareDeleteMemoryCommandTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        var promptwareDir = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware");
        Directory.CreateDirectory(promptwareDir);
        File.WriteAllText(Path.Combine(promptwareDir, "Program.md"), "# Program");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private static CommandApp BuildApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddBranch("promptware", promptware =>
            {
                promptware.AddCommand<PromptwareDeleteMemoryCommand>("delete-memory");
            });
        });
        return app;
    }

    [Fact]
    public void DeleteMemory_RemovesExistingFile()
    {
        var memoryDir = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware", "Memory");
        Directory.CreateDirectory(memoryDir);
        var staleFile = Path.Combine(memoryDir, "stale.md");
        File.WriteAllText(staleFile, "This is no longer true");

        var app = BuildApp();
        var exit = app.Run(["promptware", "delete-memory", "TestPromptware", "stale.md"]);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(staleFile));
    }

    [Fact]
    public void DeleteMemory_MissingFile_SucceedsIdempotently()
    {
        var app = BuildApp();

        var exit = app.Run(["promptware", "delete-memory", "TestPromptware", "does-not-exist.md"]);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void DeleteMemory_IgnoresPathTraversal()
    {
        var promptwareDir = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware");
        var programFile = Path.Combine(promptwareDir, "Program.md");
        Assert.True(File.Exists(programFile));

        var app = BuildApp();
        var exit = app.Run(["promptware", "delete-memory", "TestPromptware", "../../Program.md"]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(programFile));
    }

    [Fact]
    public void DeleteMemory_RejectsDotfile()
    {
        var memoryDir = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware", "Memory");
        Directory.CreateDirectory(memoryDir);
        var dotfile = Path.Combine(memoryDir, ".gitkeep");
        File.WriteAllText(dotfile, "");

        var app = BuildApp();
        var exit = app.Run(["promptware", "delete-memory", "TestPromptware", ".gitkeep"]);

        Assert.NotEqual(0, exit);
        Assert.True(File.Exists(dotfile));
    }
}
