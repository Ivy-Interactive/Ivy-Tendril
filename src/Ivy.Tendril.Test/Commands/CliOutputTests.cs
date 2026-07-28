using System.Text.Json;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test.Commands;

[Collection("TendrilHome")]
public class CliOutputTests : IDisposable
{
    public void Dispose()
    {
        CliOutput.PlainOverride = null;
    }

    private static string CaptureConsoleOut(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    // Mirrors PlanCliCommandTests.CaptureAnsiConsoleOutput: swaps AnsiConsole.Console (not
    // Console.SetOut) since AnsiConsole caches its writer on first use.
    private static string CaptureAnsiConsoleOutput(Action action)
    {
        var original = AnsiConsole.Console;
        var writer = new StringWriter();
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        try
        {
            action();
        }
        finally
        {
            AnsiConsole.Console = original;
        }

        return writer.ToString();
    }

    [Fact]
    public void WriteTable_PlainMode_EmitsAsciiTabSeparatedRows()
    {
        CliOutput.PlainOverride = true;

        var output = CaptureConsoleOut(() =>
            CliOutput.WriteTable(
                ["Name", "Prompt"],
                [
                    ["DotnetBuild", "Run dotnet build and verify zero errors"],
                    ["DotnetTest", "Run dotnet test with the plan's filter"]
                ]));

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Name\tPrompt", lines[0]);
        Assert.Equal("DotnetBuild\tRun dotnet build and verify zero errors", lines[1]);
        Assert.Equal("DotnetTest\tRun dotnet test with the plan's filter", lines[2]);
        Assert.All(output, c => Assert.True(c < 128, $"Expected only ASCII characters, found '{c}' (0x{(int)c:X4})"));
    }

    [Fact]
    public void WriteTable_NonPlainMode_RendersBorderedSpectreTable()
    {
        CliOutput.PlainOverride = false;

        var output = CaptureAnsiConsoleOutput(() =>
            CliOutput.WriteTable(
                ["Name", "Status"],
                [["DotnetBuild", "Pass"], ["DotnetTest", "Pass"]]));

        var lineCount = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        // A bordered table renders extra frame lines beyond one line per header/row,
        // which plain tab-separated output never would.
        Assert.True(lineCount > 3, $"Expected a bordered table with frame lines, got:\n{output}");
        Assert.Contains("DotnetBuild", output);
        Assert.Contains("DotnetTest", output);
        Assert.Contains("Name", output);
        Assert.Contains("Status", output);
    }

    [Fact]
    public void WriteTable_PlainMode_CollapsesEmbeddedNewlinesAndTabs()
    {
        CliOutput.PlainOverride = true;

        var output = CaptureConsoleOut(() =>
            CliOutput.WriteTable(
                ["Name", "Prompt"],
                [["Multiline", "line1\nline2\tline3\r\nline4"]]));

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // One header line + one data line, even though the cell contained embedded
        // newlines/tabs/CRLF that would otherwise fragment the row.
        Assert.Equal(2, lines.Length);
        Assert.DoesNotContain('\t', lines[1].Substring("Multiline".Length + 1));
        Assert.DoesNotContain('\n', lines[1]);
        Assert.DoesNotContain('\r', lines[1]);
    }

    [Fact]
    public void WriteTable_Utf8Content_NeverProducesReplacementCharacter()
    {
        foreach (var plain in new[] { true, false })
        {
            CliOutput.PlainOverride = plain;

            var output = plain
                ? CaptureConsoleOut(() => CliOutput.WriteTable(["Name"], [["café ✓ résumé"]]))
                : CaptureAnsiConsoleOutput(() => CliOutput.WriteTable(["Name"], [["café ✓ résumé"]]));

            Assert.DoesNotContain('�', output);
        }
    }

    [Fact]
    public void Glyph_PlainMode_IsAscii()
    {
        CliOutput.PlainOverride = true;

        Assert.Equal("OK", CliOutput.Glyph(true));
        Assert.Equal("FAIL", CliOutput.Glyph(false));
        Assert.All(CliOutput.Glyph(true) + CliOutput.Glyph(false), c => Assert.True(c < 128));
    }

    [Fact]
    public void Glyph_NonPlainMode_UsesUnicodeSymbols()
    {
        CliOutput.PlainOverride = false;

        Assert.Equal("✓", CliOutput.Glyph(true));
        Assert.Equal("✗", CliOutput.Glyph(false));
    }

    // --- verification list --json ---

    private static CommandApp BuildVerificationListApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddBranch("verification", verification => verification.AddCommand<VerificationListCommand>("list"));
        });
        return app;
    }

    [Fact]
    public void VerificationList_Json_EmitsFullUntruncatedPrompts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cli-output-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";

        try
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", tempDir);
            var longPrompt = "Run the full verification suite. " + new string('x', 200) + " End of prompt.";
            var yaml = $"""
                projects: []
                verifications:
                  - name: LongPromptCheck
                    prompt: "{longPrompt}"
                """;
            File.WriteAllText(Path.Combine(tempDir, "config.yaml"), yaml);

            var app = BuildVerificationListApp();

            var output = CaptureConsoleOut(() =>
            {
                var exit = app.Run(["verification", "list", "--json"]);
                Assert.Equal(0, exit);
            });

            var payload = JsonSerializer.Deserialize<List<JsonElement>>(output.Trim());
            Assert.NotNull(payload);
            var entry = Assert.Single(payload!);
            Assert.Equal("LongPromptCheck", entry.GetProperty("name").GetString());
            Assert.Equal(longPrompt, entry.GetProperty("prompt").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", originalTendrilHome);
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
