using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Services.Wireframes;

public enum WireframeLeakKind
{
    /// <summary>A file sits at a wireframe project's path: <c>Wireframes/&lt;name&gt;/src/</c> or <c>.wireframe/</c>.</summary>
    WireframePath,

    /// <summary>A file carries the <c>@tendril-wireframe</c> marker every scaffolded file starts with.</summary>
    Marker,

    /// <summary>A file imports, requires or depends on the <c>tendril-wireframes</c> library.</summary>
    LibraryReference,

    /// <summary>A file's content is a wireframe source file, whitespace aside.</summary>
    CopiedFile,

    /// <summary>A file contains a run of lines lifted from a wireframe source file.</summary>
    PastedLines,
}

/// <summary>One place a plan's wireframe turned up in product code.</summary>
public sealed record WireframeLeak(string Worktree, string Path, int? Line, WireframeLeakKind Kind, string Detail)
{
    public override string ToString() =>
        $"{Worktree}/{Path}{(Line is { } line ? $":{line}" : "")}: {Detail}";
}

/// <summary>
///     Finds a plan's wireframes in its product changes.
///     <para>
///         Wireframes are throwaway plan material: they show a person what will be built and guide the
///         agent building it, and nothing else. A prompt can tell an executing agent not to copy one,
///         but that agent has an unrestricted shell, so the guarantee has to come from a check it cannot
///         skip. This is that check. Tendril runs it when an execution job finishes, before a PR is
///         created, and before a plan is marked Completed.
///     </para>
///     <para>
///         It diffs every worktree under the plan's <c>Worktrees/</c> against its merge base with the
///         repo's base branch, working tree and untracked files included, and inspects each changed
///         file. Only changed files are inspected, so a repo that legitimately contains wireframe tooling
///         is not flagged for code the plan did not touch.
///     </para>
/// </summary>
public static partial class WireframeLeakGuard
{
    /// <summary>Consecutive meaningful lines a file must share with a wireframe to count as pasted.</summary>
    public const int PastedRunLength = 8;

    /// <summary>A whole-file copy is only claimed for a source with at least this many meaningful lines.</summary>
    private const int MinCopiedFileLines = 5;

    private const long MaxInspectedBytes = 1_000_000;

    private static readonly string[] SourceExtensions = [".tsx", ".ts", ".jsx", ".js", ".css", ".html"];

    [GeneratedRegex("""(\bfrom\s*|\bimport\s*\(\s*|\brequire\s*\(\s*)["']tendril-wireframes(["'/])|["']tendril-wireframes["']\s*:""")]
    private static partial Regex LibraryReferencePattern();

    /// <summary>Lines that say nothing on their own: a closing bracket, a lone tag, a blank.</summary>
    [GeneratedRegex("""^([\)\]\};,]+|</?[A-Za-z][\w.]*\s*/?>|\{?/?\*+/?\}?)$""")]
    private static partial Regex TrivialLinePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Where the last failed check is written, for the plan's failure callout.</summary>
    public static string ReportPath(string planFolder) =>
        Path.Combine(planFolder, "Verification", "WireframeLeak.md");

    /// <param name="planFolder">The plan whose worktrees and wireframes to compare.</param>
    /// <param name="baseBranchForRepo">
    ///     The configured base branch for a repo root, or null to detect the repo's default branch.
    /// </param>
    public static IReadOnlyList<WireframeLeak> Scan(string planFolder, Func<string, string?>? baseBranchForRepo = null)
    {
        var worktrees = GitHelper.EnumerateWorktreeDirectories(Path.Combine(planFolder, "Worktrees")).ToList();
        if (worktrees.Count == 0) return [];

        var fingerprints = Fingerprints.Build(Path.Combine(planFolder, PlanWireframes.FolderName));
        var leaks = new List<WireframeLeak>();

        foreach (var worktree in worktrees)
        {
            var label = Path.GetRelativePath(Path.Combine(planFolder, "Worktrees"), worktree).Replace('\\', '/');
            foreach (var relative in ChangedFiles(worktree, baseBranchForRepo))
                Inspect(worktree, label, relative, fingerprints, leaks);
        }

        return leaks;
    }

    /// <summary>A reviewer-facing explanation of <paramref name="leaks" />, for the failure callout and the CLI.</summary>
    public static string Describe(IReadOnlyList<WireframeLeak> leaks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Wireframe code was found in this plan's product changes. Wireframes are throwaway plan material: ");
        sb.AppendLine("build the screen with the project's own components instead, and remove these before the plan can move on.");
        sb.AppendLine();
        foreach (var leak in leaks)
            sb.AppendLine($"- `{leak.Worktree}/{leak.Path}{(leak.Line is { } line ? $":{line}" : "")}` {leak.Detail}");
        return sb.ToString();
    }

    /// <summary>Writes <see cref="ReportPath" /> when there are leaks and removes it when there are none.</summary>
    public static void WriteReport(string planFolder, IReadOnlyList<WireframeLeak> leaks)
    {
        var path = ReportPath(planFolder);
        if (leaks.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# Wireframe Leak\n\n## Issues Found\n\n" + Describe(leaks));
    }

    private static IEnumerable<string> ChangedFiles(string worktree, Func<string, string?>? baseBranchForRepo)
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        var repoRoot = GitHelper.ResolveRepoRootFromWorktree(worktree) ?? worktree;
        var baseBranch = baseBranchForRepo?.Invoke(repoRoot);
        if (string.IsNullOrWhiteSpace(baseBranch))
        {
            try { baseBranch = GitHelper.ResolveDefaultBranch(repoRoot); }
            catch { baseBranch = null; }
        }

        string? mergeBase = null;
        if (!string.IsNullOrWhiteSpace(baseBranch))
        {
            foreach (var candidate in new[] { $"origin/{baseBranch}", baseBranch })
            {
                var (code, output, _) = GitHelper.RunGit($"merge-base HEAD \"{candidate}\"", worktree, 30_000);
                if (code == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    mergeBase = output.Trim();
                    break;
                }
            }
        }

        // Against the merge base: every commit on the plan branch plus uncommitted edits. Without one,
        // the uncommitted edits are still checked; committed work needs a base to diff against.
        var diffTarget = mergeBase ?? "HEAD";
        AddLines(files, GitHelper.RunGit($"-c core.quotepath=off diff --name-only --diff-filter=ACMR {diffTarget}", worktree, 60_000));
        AddLines(files, GitHelper.RunGit("-c core.quotepath=off ls-files --others --exclude-standard", worktree, 60_000));

        return files;
    }

    private static void AddLines(SortedSet<string> files, (int ExitCode, string StdOut, string StdErr) result)
    {
        if (result.ExitCode != 0) return;
        foreach (var line in result.StdOut.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) files.Add(trimmed.Replace('\\', '/'));
        }
    }

    private static void Inspect(string worktree, string label, string relative, Fingerprints fingerprints, List<WireframeLeak> leaks)
    {
        var segments = relative.Split('/');

        if (segments.Contains(".wireframe", StringComparer.OrdinalIgnoreCase) || IsWireframeSourcePath(segments))
        {
            leaks.Add(new WireframeLeak(label, relative, null, WireframeLeakKind.WireframePath,
                "is a wireframe project file"));
            return;
        }

        var full = Path.Combine(worktree, relative);
        if (!File.Exists(full)) return;

        var info = new FileInfo(full);
        if (info.Length is 0 or > MaxInspectedBytes) return;

        var bytes = File.ReadAllBytes(full);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0)) return;

        var lines = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("@tendril-wireframe", StringComparison.Ordinal))
            {
                leaks.Add(new WireframeLeak(label, relative, i + 1, WireframeLeakKind.Marker,
                    "carries the @tendril-wireframe marker"));
                break;
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            if (LibraryReferencePattern().IsMatch(lines[i]))
            {
                leaks.Add(new WireframeLeak(label, relative, i + 1, WireframeLeakKind.LibraryReference,
                    "uses the tendril-wireframes library"));
                break;
            }
        }

        var meaningful = Meaningful(lines);

        if (meaningful.Count >= MinCopiedFileLines &&
            fingerprints.Files.TryGetValue(HashLines(meaningful.Select(m => m.Text)), out var copiedFrom))
        {
            leaks.Add(new WireframeLeak(label, relative, null, WireframeLeakKind.CopiedFile,
                $"is a copy of wireframe file {copiedFrom}"));
            return;
        }

        for (var start = 0; start + PastedRunLength <= meaningful.Count; start++)
        {
            var window = HashLines(meaningful.Skip(start).Take(PastedRunLength).Select(m => m.Text));
            if (fingerprints.Runs.TryGetValue(window, out var pastedFrom))
            {
                leaks.Add(new WireframeLeak(label, relative, meaningful[start].Line, WireframeLeakKind.PastedLines,
                    $"repeats {PastedRunLength} or more lines of wireframe file {pastedFrom}"));
                return;
            }
        }
    }

    /// <summary><c>…/Wireframes/&lt;name&gt;/src/…</c>, the layout <c>tendril wireframe setup</c> creates.</summary>
    private static bool IsWireframeSourcePath(string[] segments)
    {
        for (var i = 0; i + 2 < segments.Length; i++)
        {
            if (segments[i].Equals(PlanWireframes.FolderName, StringComparison.OrdinalIgnoreCase) &&
                segments[i + 2].Equals("src", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static List<(int Line, string Text)> Meaningful(string[] lines)
    {
        var result = new List<(int, string)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var normalized = Whitespace().Replace(lines[i].Trim(), " ");
            if (normalized.Length < 4 || TrivialLinePattern().IsMatch(normalized)) continue;
            result.Add((i + 1, normalized));
        }
        return result;
    }

    private static string HashLines(IEnumerable<string> lines) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));

    /// <summary>Whole-file and line-run hashes of every source file in a plan's wireframes.</summary>
    private sealed class Fingerprints
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Runs { get; } = new(StringComparer.Ordinal);

        public static Fingerprints Build(string wireframesDir)
        {
            var fingerprints = new Fingerprints();
            if (!Directory.Exists(wireframesDir)) return fingerprints;

            foreach (var project in Directory.EnumerateDirectories(wireframesDir))
            {
                var src = Path.Combine(project, "src");
                if (!Directory.Exists(src)) continue;

                foreach (var file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories))
                {
                    if (!SourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;

                    var name = Path.GetRelativePath(wireframesDir, file).Replace('\\', '/');
                    var meaningful = Meaningful(File.ReadAllText(file).Replace("\r\n", "\n").Split('\n'));

                    if (meaningful.Count >= MinCopiedFileLines)
                        fingerprints.Files.TryAdd(HashLines(meaningful.Select(m => m.Text)), name);

                    for (var start = 0; start + PastedRunLength <= meaningful.Count; start++)
                        fingerprints.Runs.TryAdd(HashLines(meaningful.Skip(start).Take(PastedRunLength).Select(m => m.Text)), name);
                }
            }

            return fingerprints;
        }
    }
}
