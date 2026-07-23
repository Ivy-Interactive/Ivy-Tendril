using Ivy.Tendril.Commands;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class PromptwareWriteCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-promptware-write-test");
    private readonly string _originalTendrilHome;

    public PromptwareWriteCommandTests()
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
                promptware.AddCommand<PromptwareWriteMemoryCommand>("write-memory");
                promptware.AddCommand<PromptwareWriteToolCommand>("write-tool");
            });
        });
        return app;
    }

    // --- write-memory ---

    [Fact]
    public void WriteMemory_File_ReadsFromFile()
    {
        var file = Path.Combine(_tempDir.Path, "content.md");
        File.WriteAllText(file, "Memory from file");
        var app = BuildApp();

        var exit = app.Run(["promptware", "write-memory", "TestPromptware", "pattern.md", "--file", file]);

        Assert.Equal(0, exit);
        var written = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware", "Memory", "pattern.md");
        Assert.Equal("Memory from file", File.ReadAllText(written));
    }

    [Fact]
    public void WriteMemory_Stdin_ReadsPipedInput()
    {
        var app = BuildApp();

        var originalIn = Console.In;
        Console.SetIn(new StringReader("Memory from stdin"));
        try
        {
            var exit = app.Run(["promptware", "write-memory", "TestPromptware", "pattern-stdin.md", "--stdin"]);
            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var written = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware", "Memory", "pattern-stdin.md");
        Assert.Equal("Memory from stdin", File.ReadAllText(written));
    }

    [Fact]
    public void WriteMemory_MultipleSources_FailsValidation()
    {
        Assert.False(new PromptwareWriteMemorySettings
        {
            Name = "TestPromptware",
            Filename = "f.md",
            FilePath = "f.md",
            Stdin = true
        }.Validate().Successful);

        var app = BuildApp();
        Assert.Throws<CommandRuntimeException>(() =>
            app.Run(["promptware", "write-memory", "TestPromptware", "f.md", "--file", "f.md", "--stdin"]));
    }

    [Fact]
    public void WriteMemory_NoSource_ThrowsAndNeverReadsStdin()
    {
        var app = BuildApp();

        var originalIn = Console.In;
        Console.SetIn(new StringReader("SENTINEL-SHOULD-NOT-BE-READ"));
        try
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                app.Run(["promptware", "write-memory", "TestPromptware", "no-source.md"]));
            Assert.Contains("--file or --stdin", ex.Message);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var written = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware", "Memory", "no-source.md");
        Assert.False(File.Exists(written));
    }

    // --- write-tool ---

    [Fact]
    public void WriteTool_File_ReadsFromFile()
    {
        var file = Path.Combine(_tempDir.Path, "tool-content.md");
        File.WriteAllText(file, "Tool from file");
        var app = BuildApp();

        var exit = app.Run(["promptware", "write-tool", "TestPromptware", "tool.md", "--file", file]);

        Assert.Equal(0, exit);
        var written = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware", "Tools", "tool.md");
        Assert.Equal("Tool from file", File.ReadAllText(written));
    }

    [Fact]
    public void WriteTool_Stdin_ReadsPipedInput()
    {
        var app = BuildApp();

        var originalIn = Console.In;
        Console.SetIn(new StringReader("Tool from stdin"));
        try
        {
            var exit = app.Run(["promptware", "write-tool", "TestPromptware", "tool-stdin.md", "--stdin"]);
            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var written = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware", "Tools", "tool-stdin.md");
        Assert.Equal("Tool from stdin", File.ReadAllText(written));
    }

    [Fact]
    public void WriteTool_MultipleSources_FailsValidation()
    {
        Assert.False(new PromptwareWriteToolSettings
        {
            Name = "TestPromptware",
            Filename = "f.md",
            FilePath = "f.md",
            Stdin = true
        }.Validate().Successful);

        var app = BuildApp();
        Assert.Throws<CommandRuntimeException>(() =>
            app.Run(["promptware", "write-tool", "TestPromptware", "f.md", "--file", "f.md", "--stdin"]));
    }

    [Fact]
    public void WriteTool_NoSource_ThrowsAndNeverReadsStdin()
    {
        var app = BuildApp();

        var originalIn = Console.In;
        Console.SetIn(new StringReader("SENTINEL-SHOULD-NOT-BE-READ"));
        try
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                app.Run(["promptware", "write-tool", "TestPromptware", "no-source.md"]));
            Assert.Contains("--file or --stdin", ex.Message);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var written = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware", "Tools", "no-source.md");
        Assert.False(File.Exists(written));
    }
}
