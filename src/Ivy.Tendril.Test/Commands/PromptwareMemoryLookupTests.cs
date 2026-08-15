using Ivy.Tendril.Commands;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test.Commands;

[Collection("TendrilHome")]
public class PromptwareMemoryLookupTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-promptware-memory-test");
    private readonly string _originalTendrilHome;
    private readonly string _memoryDir;

    public PromptwareMemoryLookupTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        var promptwareDir = Path.Combine(_tempDir.Path, "Promptwares", "TestPromptware");
        Directory.CreateDirectory(promptwareDir);
        File.WriteAllText(Path.Combine(promptwareDir, "Program.md"), "# Program");

        _memoryDir = Path.Combine(promptwareDir, "Memory");
        Directory.CreateDirectory(_memoryDir);
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
                promptware.AddCommand<PromptwareReadMemoryCommand>("read-memory");
                promptware.AddCommand<PromptwareListMemoryCommand>("list-memory");
            });
        });
        return app;
    }

    private (int Exit, string Output) CaptureConsoleOut(Func<int> run)
    {
        lock (TestLocks.ConsoleLock)
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);
            var output = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(output);
            try
            {
                var exit = run();
                return (exit, output.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    // --- read-memory ---

    [Fact]
    public void ReadMemory_ExactName_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "pattern.md"), "Memory content");
        var app = BuildApp();

        var (exit, output) = CaptureConsoleOut(() =>
            app.Run(["promptware", "read-memory", "TestPromptware", "pattern.md"]));

        Assert.Equal(0, exit);
        Assert.Equal("Memory content", output);
    }

    [Fact]
    public void ReadMemory_NameWithoutExtension_ResolvesMdFile()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "coalmininggame-pnpm.md"), "pnpm notes");
        var app = BuildApp();

        var (exit, output) = CaptureConsoleOut(() =>
            app.Run(["promptware", "read-memory", "TestPromptware", "coalmininggame-pnpm"]));

        Assert.Equal(0, exit);
        Assert.Equal("pnpm notes", output);
    }

    [Fact]
    public void ReadMemory_WikiLinkReference_ResolvesMdFile()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "coalmininggame-pnpm.md"), "pnpm notes");
        var app = BuildApp();

        var (exit, output) = CaptureConsoleOut(() =>
            app.Run(["promptware", "read-memory", "TestPromptware", "[[coalmininggame-pnpm]]"]));

        Assert.Equal(0, exit);
        Assert.Equal("pnpm notes", output);
    }

    [Fact]
    public void ReadMemory_DifferentCase_ResolvesFile()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "coalmininggame-pnpm.md"), "pnpm notes");
        var app = BuildApp();

        var (exit, output) = CaptureConsoleOut(() =>
            app.Run(["promptware", "read-memory", "TestPromptware", "CoalMiningGame-PNPM.md"]));

        Assert.Equal(0, exit);
        Assert.Equal("pnpm notes", output);
    }

    [Fact]
    public void ReadMemory_NonExistentFile_Throws()
    {
        var app = BuildApp();

        Assert.Throws<FileNotFoundException>(() =>
            app.Run(["promptware", "read-memory", "TestPromptware", "nonexistent.md"]));
    }

    [Fact]
    public void ReadMemory_UnknownPromptware_Throws()
    {
        var app = BuildApp();

        Assert.Throws<FileNotFoundException>(() =>
            app.Run(["promptware", "read-memory", "NoSuchPromptware", "pattern.md"]));
    }

    [Fact]
    public void ReadMemory_NoArguments_FailsValidation()
    {
        var app = BuildApp();

        Assert.Throws<CommandRuntimeException>(() =>
            app.Run(["promptware", "read-memory"]));
    }

    [Fact]
    public void ReadMemory_PathTraversalAttempt_Throws()
    {
        var app = BuildApp();

        Assert.Throws<FileNotFoundException>(() =>
            app.Run(["promptware", "read-memory", "TestPromptware", "../../etc/passwd"]));
    }

    // --- list-memory ---

    [Fact]
    public void ListMemory_PrintsOneFilePerLine_ExcludingDotfiles()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "b-memory.md"), "b");
        File.WriteAllText(Path.Combine(_memoryDir, "a-memory.md"), "a");
        File.WriteAllText(Path.Combine(_memoryDir, ".hidden.md"), "hidden");
        var app = BuildApp();

        var (exit, output) = CaptureConsoleOut(() =>
            app.Run(["promptware", "list-memory", "TestPromptware"]));

        Assert.Equal(0, exit);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["a-memory.md", "b-memory.md"], lines);
    }

    [Fact]
    public void ListMemory_EmptyFolder_ExitsZeroWithNoOutput()
    {
        var app = BuildApp();

        var (exit, output) = CaptureConsoleOut(() =>
            app.Run(["promptware", "list-memory", "TestPromptware"]));

        Assert.Equal(0, exit);
        Assert.Equal("", output);
    }
}
