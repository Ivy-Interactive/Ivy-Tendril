using Ivy.Tendril.Commands;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class TrashWriteCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-trash-write-test");
    private readonly string _originalTendrilHome;

    public TrashWriteCommandTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);
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
            config.AddBranch("trash", trash => trash.AddCommand<TrashWriteCommand>("write"));
        });
        return app;
    }

    [Fact]
    public void Write_File_ReadsFromFile()
    {
        var file = Path.Combine(_tempDir.Path, "content.md");
        File.WriteAllText(file, "Trash content from file");
        var app = BuildApp();

        var exit = app.Run(["trash", "write", "DuplicateTitle.md", "--file", file]);

        Assert.Equal(0, exit);
        var written = Path.Combine(_tempDir.Path, "Trash", "DuplicateTitle.md");
        Assert.Equal("Trash content from file", File.ReadAllText(written));
    }

    [Fact]
    public void Write_Stdin_ReadsPipedInput()
    {
        var app = BuildApp();

        var originalIn = Console.In;
        Console.SetIn(new StringReader("Trash content from stdin"));
        try
        {
            var exit = app.Run(["trash", "write", "StdinTitle.md", "--stdin"]);
            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var written = Path.Combine(_tempDir.Path, "Trash", "StdinTitle.md");
        Assert.Equal("Trash content from stdin", File.ReadAllText(written));
    }

    [Fact]
    public void Write_MultipleSources_FailsValidation()
    {
        Assert.False(new TrashWriteSettings { Filename = "f.md", FilePath = "f.md", Stdin = true }.Validate().Successful);

        var app = BuildApp();
        Assert.Throws<CommandRuntimeException>(() =>
            app.Run(["trash", "write", "f.md", "--file", "f.md", "--stdin"]));
    }

    [Fact]
    public void Write_NoSource_ThrowsAndNeverReadsStdin()
    {
        var app = BuildApp();

        var originalIn = Console.In;
        Console.SetIn(new StringReader("SENTINEL-SHOULD-NOT-BE-READ"));
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => app.Run(["trash", "write", "NoSource.md"]));
            Assert.Contains("--file or --stdin", ex.Message);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var written = Path.Combine(_tempDir.Path, "Trash", "NoSource.md");
        Assert.False(File.Exists(written));
    }
}
