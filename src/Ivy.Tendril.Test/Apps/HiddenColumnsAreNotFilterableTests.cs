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
    private static readonly Regex ColumnSelector =
        new(@"^\s*(?<p>\w+)\s*=>\s*\k<p>\.(?<prop>\w+)\s*$", RegexOptions.Compiled);

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

    [Fact]
    public void EveryHiddenSelectorIsReadable()
    {
        var repoRoot = RepoRoot.Find();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductFiles(repoRoot))
        {
            var source = File.ReadAllText(file);
            var relativePath = Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/');

            foreach (var (line, args) in ParseHiddenCalls(source, relativePath))
            {
                foreach (var arg in args)
                {
                    if (!ColumnSelector.IsMatch(arg))
                    {
                        offenders.Add($"{relativePath}:{line} - argument: {arg.Trim()}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Hidden column selector(s) that cannot be parsed as plain 't => t.X' lambda:\n"
            + string.Join("\n", offenders.Select(o => "  " + o)) + "\n"
            + "Write the selector as a literal lambda, or extend the census to recognize the shape.");
    }

    [Fact]
    public void TheScanCoversEveryProjectUnderSrc()
    {
        var repoRoot = RepoRoot.Find();
        var srcDir = Path.Combine(repoRoot, "src");

        // Projects we deliberately exclude from scanning
        var notScanned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Test projects are excluded - they quote .Hidden(...) in failure messages
            ["Ivy.Tendril.Test"] = "Test project with Hidden(...) in failure messages",
            ["Ivy.Tendril.Agents.Test"] = "Test project",
            ["Ivy.Tendril.Test.End2End"] = "E2E test project",
            ["Ivy.Tendril.Agents.Test.End2End"] = "E2E test project",
            // Non-C# directories
            [".releases"] = "Release artifacts, not code",
            ["news"] = "Documentation, not code",
            ["resend"] = "Scripts, not code",
            ["scripts"] = "Scripts, not code",
        };

        // Find all immediate children of src/ with at least one .cs file
        var projectsWithCode = Directory.EnumerateDirectories(srcDir)
            .Where(dir =>
            {
                var dirName = Path.GetFileName(dir);
                // Skip hidden directories
                if (dirName.StartsWith(".") && dirName != ".releases")
                {
                    return false;
                }
                // Has at least one .cs file (excluding bin/obj)
                return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                    .Any(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                              !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
            })
            .Select(dir => Path.GetFileName(dir))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Projects that should be scanned (non-test projects with code, not in NotScanned)
        var shouldBeScanned = projectsWithCode.Except(notScanned.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        // Projects that are scanned by EnumerateProductFiles
        var scannedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateProductFiles(repoRoot))
        {
            var relativePath = Path.GetRelativePath(repoRoot, file);
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            if (parts.Length >= 2 && parts[0].Equals("src", StringComparison.OrdinalIgnoreCase))
            {
                scannedProjects.Add(parts[1]);
            }
        }

        // Check: every project with code is either scanned or has a reason
        var missingReasons = projectsWithCode.Except(scannedProjects.Union(notScanned.Keys, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(missingReasons.Count == 0,
            "Project(s) under src/ with .cs files but neither scanned nor documented in NotScanned:\n"
            + string.Join("\n", missingReasons.Select(p => "  " + p)));

        // Check: every NotScanned entry still exists
        var obsoleteReasons = notScanned.Keys.Except(projectsWithCode).ToList();
        Assert.True(obsoleteReasons.Count == 0,
            "NotScanned entries for projects that no longer exist or have no .cs files:\n"
            + string.Join("\n", obsoleteReasons.Select(p => "  " + p)));

        // Check: every project that should be scanned is actually scanned
        var unscannedProjects = shouldBeScanned.Except(scannedProjects, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(unscannedProjects.Count == 0,
            "Project(s) that should be scanned but are not included by EnumerateProductFiles:\n"
            + string.Join("\n", unscannedProjects.Select(p => "  " + p)));
    }

    [Theory]
    [InlineData(".Hidden(t => t.A)", new[] { "A" }, new string[] { })]
    [InlineData(".Hidden(t => t.A, t => t.B)", new[] { "A", "B" }, new string[] { })]
    [InlineData(".Hidden(t => t.A,\n    t => t.B)", new[] { "A", "B" }, new string[] { })]
    [InlineData(".Hidden(t => t.Foo())", new string[] { }, new[] { "t => t.Foo()" })]
    [InlineData(".Hidden(t => t.A, t => t.B())", new[] { "A" }, new[] { "t => t.B()" })]
    [InlineData(".Hidden(HiddenColumns)", new string[] { }, new[] { "HiddenColumns" })]
    [InlineData(".Hidden(t => t.A", new string[] { }, new[] { "unterminated" })]
    [InlineData(".Filterable(t => t.A, false)", new[] { "A" }, new string[] { })]
    [InlineData(".Filterable(t => t.A, hide)", new string[] { }, new[] { "hide" })]
    public void ParseHiddenCallsHandlesSyntheticFixtures(string source, string[] expectedReadable, string[] expectedUnreadable)
    {
        var (readable, unreadable) = ParseSingleHiddenOrFilterable(source, isHidden: source.Contains(".Hidden("));

        Assert.Equal(expectedReadable.OrderBy(s => s).ToArray(), readable.OrderBy(s => s).ToArray());

        if (expectedUnreadable.Length == 0)
        {
            Assert.Empty(unreadable);
        }
        else if (expectedUnreadable[0] == "unterminated")
        {
            Assert.Single(unreadable);
            Assert.Contains("unterminated", unreadable[0], StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(expectedUnreadable.Length, unreadable.Count);
        }
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
        var census = new Dictionary<string, Columns>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateProductFiles(repoRoot))
        {
            var source = File.ReadAllText(file);

            // Hidden takes params, so the selector list has to be parsed rather than assumed to be single.
            var hidden = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, args) in ParseHiddenCalls(source, Path.GetFileName(file)))
            {
                foreach (var arg in args)
                {
                    var match = ColumnSelector.Match(arg);
                    if (match.Success)
                    {
                        hidden.Add(match.Groups["prop"].Value);
                    }
                }
            }

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

    /// <summary>
    ///     Enumerate .cs files in all non-test projects under src/, skipping bin, obj, node_modules,
    ///     and generated files.
    /// </summary>
    private static IEnumerable<string> EnumerateProductFiles(string repoRoot)
    {
        var srcDir = Path.Combine(repoRoot, "src");

        // Enumerate all non-test project directories under src/
        var projectDirs = Directory.EnumerateDirectories(srcDir)
            .Where(dir =>
            {
                var dirName = Path.GetFileName(dir);
                // Exclude test projects
                return !dirName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase) &&
                       !dirName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) &&
                       !dirName.EndsWith(".End2End", StringComparison.OrdinalIgnoreCase) &&
                       // Exclude non-code directories
                       !dirName.Equals(".releases", StringComparison.OrdinalIgnoreCase) &&
                       !dirName.Equals("news", StringComparison.OrdinalIgnoreCase) &&
                       !dirName.Equals("resend", StringComparison.OrdinalIgnoreCase) &&
                       !dirName.Equals("scripts", StringComparison.OrdinalIgnoreCase);
            });

        foreach (var projectDir in projectDirs)
        {
            foreach (var file in EnumerateFilesRecursive(projectDir, "*.cs"))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    ///     Recursively enumerate files matching a pattern, skipping bin, obj, and node_modules directories,
    ///     and skipping generated .g.cs files.
    /// </summary>
    private static IEnumerable<string> EnumerateFilesRecursive(string directory, string pattern)
    {
        var dirName = Path.GetFileName(directory);
        if (dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, pattern))
        {
            // Skip generated files
            if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }

        foreach (var subDir in Directory.EnumerateDirectories(directory))
        {
            foreach (var file in EnumerateFilesRecursive(subDir, pattern))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    ///     Parse .Hidden(...) calls in source text, returning the line number and argument list for each.
    ///     An unterminated call (no balanced closing paren) returns a diagnostic instead of arguments.
    /// </summary>
    private static IEnumerable<(int line, List<string> args)> ParseHiddenCalls(string source, string fileName)
    {
        var index = 0;
        while ((index = source.IndexOf(".Hidden(", index, StringComparison.Ordinal)) >= 0)
        {
            var lineNumber = source.Substring(0, index).Count(c => c == '\n') + 1;
            var openParen = index + ".Hidden(".Length - 1; // Position of '('

            var (args, terminated) = ParseArgumentList(source, openParen);

            if (!terminated)
            {
                yield return (lineNumber, new List<string> { $"unterminated .Hidden( call in {fileName}" });
            }
            else
            {
                yield return (lineNumber, args);
            }

            index = openParen + 1;
        }
    }

    /// <summary>
    ///     Test helper: parse a single .Hidden(...) or .Filterable(...) call from a synthetic fixture.
    ///     Returns (readable selectors, unreadable arguments).
    /// </summary>
    private static (List<string> readable, List<string> unreadable) ParseSingleHiddenOrFilterable(string source, bool isHidden)
    {
        var marker = isHidden ? ".Hidden(" : ".Filterable(";
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return (new List<string>(), new List<string>());
        }

        var openParen = index + marker.Length - 1;
        var (args, terminated) = ParseArgumentList(source, openParen);

        if (!terminated)
        {
            return (new List<string>(), new List<string> { "unterminated call" });
        }

        var readable = new List<string>();
        var unreadable = new List<string>();

        if (isHidden)
        {
            // For .Hidden, all arguments should be column selectors
            foreach (var arg in args)
            {
                var match = ColumnSelector.Match(arg);
                if (match.Success)
                {
                    readable.Add(match.Groups["prop"].Value);
                }
                else
                {
                    unreadable.Add(arg.Trim());
                }
            }
        }
        else
        {
            // For .Filterable, first arg should be a selector, second should be 'false'
            if (args.Count >= 2)
            {
                var match = ColumnSelector.Match(args[0]);
                if (match.Success && args[1].Trim() == "false")
                {
                    readable.Add(match.Groups["prop"].Value);
                }
                else
                {
                    if (!match.Success)
                    {
                        unreadable.Add(args[0].Trim());
                    }
                    if (args[1].Trim() != "false")
                    {
                        unreadable.Add(args[1].Trim());
                    }
                }
            }
        }

        return (readable, unreadable);
    }

    /// <summary>
    ///     Parse an argument list starting from an open paren, walking forward counting depth,
    ///     skipping over quoted strings, and splitting on commas at depth 1.
    ///     Returns (arguments, wasTerminated).
    /// </summary>
    private static (List<string> args, bool terminated) ParseArgumentList(string source, int openParenIndex)
    {
        var args = new List<string>();
        var depth = 0;
        var argStart = openParenIndex + 1;
        var i = openParenIndex;
        var length = source.Length;

        while (i < length)
        {
            var c = source[i];

            if (c == '"' || c == '\'')
            {
                // Skip over quoted string
                var quote = c;
                i++;
                while (i < length)
                {
                    if (source[i] == '\\' && i + 1 < length)
                    {
                        i += 2; // Skip escaped character
                    }
                    else if (source[i] == quote)
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        i++;
                    }
                }
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    // Found the closing paren - extract the last argument
                    var lastArg = source.Substring(argStart, i - argStart);
                    if (!string.IsNullOrWhiteSpace(lastArg))
                    {
                        args.Add(lastArg);
                    }
                    return (args, true);
                }
            }
            else if (c == ',' && depth == 1)
            {
                // Found an argument separator at the top level
                var arg = source.Substring(argStart, i - argStart);
                args.Add(arg);
                argStart = i + 1;
            }

            i++;
        }

        // Reached end of source without finding closing paren
        return (args, false);
    }

    private sealed record Columns(HashSet<string> Hidden, HashSet<string> NonFilterable);
}
