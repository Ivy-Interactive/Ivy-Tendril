using System.Text.RegularExpressions;
using Spectre.Console;

namespace Ivy.Tendril.Helpers;

public static class CliOutput
{
    // Test-only seam: lets CliOutputTests force plain/rich rendering regardless of the
    // ambient Console.IsOutputRedirected state (test runners redirect stdout by default).
    internal static bool? PlainOverride { get; set; }

    public static bool IsPlain => PlainOverride ?? (Console.IsOutputRedirected || Console.OutputEncoding.CodePage != 65001);

    public static void WriteTable(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, TableBorder? border = null)
    {
        if (IsPlain)
        {
            Console.Out.WriteLine(string.Join('\t', headers.Select(SanitizeCell)));
            foreach (var row in rows)
                Console.Out.WriteLine(string.Join('\t', row.Select(SanitizeCell)));
            return;
        }

        var table = new Spectre.Console.Table();
        table.Border(border ?? TableBorder.Rounded);
        foreach (var header in headers)
            table.AddColumn(header);
        foreach (var row in rows)
            table.AddRow(row.Select(cell => cell.EscapeMarkup()).ToArray());

        AnsiConsole.Write(table);
    }

    public static string Glyph(bool ok) => IsPlain ? (ok ? "OK" : "FAIL") : (ok ? "✓" : "✗");

    private static string SanitizeCell(string value) => Regex.Replace(value, @"[\r\n\t]+", " ");
}
