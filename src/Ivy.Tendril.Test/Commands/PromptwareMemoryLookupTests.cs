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

    // --- read-memory ---

    [Fact]
    public void ReadMemory_ExactName_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "pattern.md"), "Memory content");
        var app = BuildApp();

        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        int exit;
        try
        {
            exit = app.Run(["promptware", "read-memory", "TestPromptware", "pattern.md"]);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, exit);
        Assert.Equal("Memory content", output.ToString());
    }

    [Fact]
    public void ReadMemory_NameWithoutExtension_ResolvesMdFile()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "coalmininggame-pnpm.md"), "pnpm notes");
        var app = BuildApp();

        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        int exit;
        try
        {
            exit = app.Run(["promptware", "read-memory", "TestPromptware", "coalmininggame-pnpm"]);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, exit);
        Assert.Equal("pnpm notes", output.ToString());
    }

    [Fact]
    public void ReadMemory_WikiLinkReference_ResolvesMdFile()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "coalmininggame-pnpm.md"), "pnpm notes");
        var app = BuildApp();

        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        int exit;
        try
        {
            exit = app.Run(["promptware", "read-memory", "TestPromptware", "[[coalmininggame-pnpm]]"]);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, exit);
        Assert.Equal("pnpm notes", output.ToString());
    }

    [Fact]
    public void ReadMemory_DifferentCase_ResolvesFile()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "coalmininggame-pnpm.md"), "pnpm notes");
        var app = BuildApp();

        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        int exit;
        try
        {
            exit = app.Run(["promptware", "read-memory", "TestPromptware", "CoalMiningGame-PNPM.md"]);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, exit);
        Assert.Equal("pnpm notes", output.ToString());
    }

    [Fact]
    public void ReadMemory_Missing_ListsAvailableAndSuggests()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "vite-plus-check-workflow.md"), "workflow notes");
        var app = BuildApp();

        var ex = Assert.Throws<FileNotFoundException>(() =>
            app.Run(["promptware", "read-memory", "TestPromptware", "voidzero-vite-plus-stack"]));

        Assert.Contains("Available memories: vite-plus-check-workflow.md", ex.Message);
        Assert.Contains("Did you mean: vite-plus-check-workflow.md?", ex.Message);
        Assert.Contains("may have been pruned", ex.Message);
    }

    [Fact]
    public void ReadMemory_Missing_EmptyFolder_ReportsNone()
    {
        var app = BuildApp();

        var ex = Assert.Throws<FileNotFoundException>(() =>
            app.Run(["promptware", "read-memory", "TestPromptware", "anything.md"]));

        Assert.Contains("Available memories: (none)", ex.Message);
        Assert.DoesNotContain("Did you mean", ex.Message);
    }

    [Fact]
    public void ReadMemory_UnknownPromptware_ReportsPromptwareNotFound()
    {
        var app = BuildApp();

        var ex = Assert.Throws<FileNotFoundException>(() =>
            app.Run(["promptware", "read-memory", "NoSuchPromptware", "anything.md"]));

        Assert.Contains("Promptware not found: NoSuchPromptware", ex.Message);
        Assert.DoesNotContain("Memory file not found", ex.Message);
    }

    [Fact]
    public void ReadMemory_PathTraversal_DoesNotEscapeMemoryFolder()
    {
        var app = BuildApp();

        var ex = Assert.Throws<FileNotFoundException>(() =>
            app.Run(["promptware", "read-memory", "TestPromptware", "../Program.md"]));

        Assert.Contains("Memory file not found: Program.md", ex.Message);
    }

    // --- list-memory ---

    [Fact]
    public void ListMemory_PrintsOneFilePerLine_ExcludingDotfiles()
    {
        File.WriteAllText(Path.Combine(_memoryDir, "b-memory.md"), "b");
        File.WriteAllText(Path.Combine(_memoryDir, "a-memory.md"), "a");
        File.WriteAllText(Path.Combine(_memoryDir, ".hidden.md"), "hidden");
        var app = BuildApp();

        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        int exit;
        try
        {
            exit = app.Run(["promptware", "list-memory", "TestPromptware"]);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, exit);
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["a-memory.md", "b-memory.md"], lines);
    }

    [Fact]
    public void ListMemory_EmptyFolder_ExitsZeroWithNoOutput()
    {
        var app = BuildApp();

        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        int exit;
        try
        {
            exit = app.Run(["promptware", "list-memory", "TestPromptware"]);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, exit);
        Assert.Equal("", output.ToString());
    }
}
