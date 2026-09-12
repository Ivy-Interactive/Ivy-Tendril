using System.Text.RegularExpressions;
using Ivy.Tendril.Test.TestHelpers;
using Xunit;

namespace Ivy.Tendril.Test.Apps;

/// <summary>
///     <c>Hidden</c> and <c>Filterable</c> are independent settings on an Ivy <c>DataTable</c> column: the
///     frontend builds the filter dropdown from <c>filterable</c> alone and never consults <c>hidden</c>, and
///     <c>Filterable</c> defaults to <c>true</c>. So a column that is only hidden is still offered as a filter
///     target the user cannot see. Every hidden column therefore needs a matching
///     <c>.Filterable(t =&gt; t.X, false)</c>, and this test fails when one is added without it.
///     Pinned as source text because the columns live in a private dictionary of a private nested type inside
///     the Ivy builder and are only surfaced from <c>Build()</c>, which needs a view <c>Context</c>.
/// </summary>
public class HiddenColumnsAreNotFilterableTests
{
    private static readonly Regex HiddenCall = new(@"\.Hidden\((?<args>[^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex Selector = new(@"(?<p>\w+)\s*=>\s*\k<p>\.(?<prop>\w+)", RegexOptions.Compiled);

    private static readonly Regex NonFilterable =
        new(@"\.Filterable\(\s*(?<p>\w+)\s*=>\s*\k<p>\.(?<prop>\w+)\s*,\s*false\s*\)", RegexOptions.Compiled);

    [Fact]
    public void EveryHiddenColumnIsAlsoNonFilterable()
    {
        var offenders = new List<string>();

        foreach (var (relativePath, columns) in Census())
        {
            foreach (var property in columns.Hidden.Except(columns.NonFilterable).OrderBy(p => p, StringComparer.Ordinal))
            {
                offenders.Add($"{relativePath}: {property}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Hidden DataTable column(s) with no matching Filterable(..., false):\n"
            + string.Join("\n", offenders.Select(o => "  " + o)) + "\n"
            + "A hidden column stays filterable, so it appears in the filter dropdown as a column the user\n"
            + "cannot see. Add .Filterable(t => t.X, false) beside each .Hidden(t => t.X).");
    }

    [Fact]
    public void TheAuditActuallySeesTheJobsTableColumns()
    {
        // Without this, a regex that stops matching makes the fact above pass vacuously.
        var census = Census();
        const string jobsTable = "src/Ivy.Tendril/Apps/Jobs/JobsApp.DataTable.cs";

        Assert.True(census.TryGetValue(jobsTable, out var columns),
            $"The census found no hidden columns in {jobsTable}. Its .Hidden(...) calls, or the regexes in "
            + "this test, have changed - the guard above is now vacuous.");

        Assert.Contains("Id", columns!.Hidden);
        Assert.Contains("ErrorContext", columns.Hidden);
        Assert.Contains("Id", columns.NonFilterable);
        Assert.Contains("ErrorContext", columns.NonFilterable);
    }

    /// <summary>
    ///     Hidden and explicitly-non-filterable column names per source file, keyed by repo-relative path with
    ///     forward slashes. Files declaring no hidden column are omitted. Paired per file rather than per
    ///     builder chain: a chain has no delimiter in source text, and the property names of two tables in one
    ///     file do not collide in a way that matters here.
    /// </summary>
    private static Dictionary<string, Columns> Census()
    {
        var repoRoot = RepoRoot.Find();
        var productDir = Path.Combine(repoRoot, "src", "Ivy.Tendril");
        var census = new Dictionary<string, Columns>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(productDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                                 !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var source = File.ReadAllText(file);

            // Hidden takes params, so the selector list has to be parsed rather than assumed to be single.
            var hidden = HiddenCall.Matches(source)
                .SelectMany(m => Selector.Matches(m.Groups["args"].Value))
                .Select(m => m.Groups["prop"].Value)
                .ToHashSet(StringComparer.Ordinal);

            if (hidden.Count == 0)
            {
                continue;
            }

            var nonFilterable = NonFilterable.Matches(source)
                .Select(m => m.Groups["prop"].Value)
                .ToHashSet(StringComparer.Ordinal);

            var relativePath = Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            census[relativePath] = new Columns(hidden, nonFilterable);
        }

        return census;
    }

    private sealed record Columns(HashSet<string> Hidden, HashSet<string> NonFilterable);
}
