using System.Text.RegularExpressions;
using Ivy.Tendril.Models;
using Ivy.Tendril.Test.TestHelpers;

namespace Ivy.Tendril.Test.Models;

/// <summary>
///     <see cref="JobExclusionGroup.PlanIssue" /> exists because its members read the plan and mutate
///     GitHub but never write inside the plan folder, per <see cref="JobArgs" />'s doc comment on
///     <c>CreateIssueArgs.ExclusionGroup</c>. That premise is not enforced anywhere else: it holds only
///     as long as no promptware in the group ever calls a mutating <c>tendril plan</c> subcommand. This
///     audits every promptware currently in the group against that premise, so growing a plan-folder
///     write is a failing test here rather than a silent trap for whoever moves it into
///     <see cref="JobExclusionGroup.PlanWorktree" /> later.
/// </summary>
public class PlanIssueGroupPromptwareAuditTests
{
    /// <summary>
    ///     Read-only <c>tendril plan</c> subcommands, taken from <c>tendril plan --help</c> and its
    ///     <c>rec</c> / <c>env</c> / <c>verification</c> subgroups. An allow-list rather than a
    ///     deny-list, so a new mutating subcommand added to the CLI later fails this audit by default
    ///     instead of silently slipping through.
    /// </summary>
    private static readonly HashSet<string> ReadOnlyPlanSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "list", "get", "get-revision", "validate", "doctor",
        "rec list", "env get", "verification list"
    };

    /// <summary>
    ///     Matches the two-word form first, since <c>rec</c>, <c>env</c> and <c>verification</c> are
    ///     groups: <c>tendril plan rec</c> alone (with nothing after it) is not a verdict either way.
    /// </summary>
    private static readonly Regex PlanInvocation =
        new(@"tendril\s+plan\s+([a-z][a-z-]*)(?:\s+([a-z][a-z-]*))?", RegexOptions.Compiled);

    private const string PlanReadOnlyMarker =
        "Do NOT write anything into the plan folder, including plan.yaml, revisions and artifacts.";

    [Fact]
    public void PlanIssuePromptwares_MakeNoPlanFolderWrites()
    {
        var repoRoot = RepoRoot.Find();

        foreach (var (relativePath, source) in EnumeratePlanIssuePromptwareFiles(repoRoot))
        {
            foreach (Match match in PlanInvocation.Matches(source))
            {
                var twoWord = match.Groups[2].Success ? $"{match.Groups[1].Value} {match.Groups[2].Value}" : null;
                var oneWord = match.Groups[1].Value;

                var isReadOnly = (twoWord != null && ReadOnlyPlanSubcommands.Contains(twoWord)) ||
                                  ReadOnlyPlanSubcommands.Contains(oneWord);
                var subcommand = twoWord ?? oneWord;

                Assert.True(isReadOnly,
                    "CreateIssue is in JobExclusionGroup.PlanIssue, which exists because it does not write inside\n"
                    + $"the plan folder: {relativePath} now runs '{match.Value.Trim()}'.\n"
                    + "A plan-folder writer races ExecutePlan, CreatePr and the revision writers, so either drop the\n"
                    + "write, or move CreateIssueArgs.ExclusionGroup to JobExclusionGroup.PlanWorktree, move the name\n"
                    + "in JobExclusionGroups.Members, and add the type to PlanMutatingTypes_ShareOneExclusionGroup.\n"
                    + $"If '{subcommand}' is in fact read-only, add it to ReadOnlyPlanSubcommands here.");
            }
        }
    }

    [Fact]
    public void PlanIssuePromptwares_DeclareThePlanReadOnlyRule()
    {
        var repoRoot = RepoRoot.Find();

        foreach (var jobType in JobExclusionGroups.TypesIn(JobExclusionGroup.PlanIssue))
        {
            var programPath = Path.Combine(repoRoot, "src", "Ivy.Tendril", "Promptwares", jobType, "Program.md");
            Assert.True(File.Exists(programPath), $"Expected {programPath} to exist for job type {jobType}.");

            var normalized = Regex.Replace(File.ReadAllText(programPath), @"\s+", " ");

            Assert.Contains(PlanReadOnlyMarker, normalized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PlanIssueGroup_IsNotEmpty()
    {
        // Emptying or renaming the group would otherwise make the two facts above vacuously pass. If the
        // group is legitimately retired, this is what tells the author to delete this file rather than
        // leave a test that asserts nothing.
        Assert.NotEmpty(JobExclusionGroups.TypesIn(JobExclusionGroup.PlanIssue));
    }

    private static List<(string RelativePath, string Source)> EnumeratePlanIssuePromptwareFiles(string repoRoot)
    {
        var files = new List<(string RelativePath, string Source)>();

        foreach (var jobType in JobExclusionGroups.TypesIn(JobExclusionGroup.PlanIssue))
        {
            var promptwareDir = Path.Combine(repoRoot, "src", "Ivy.Tendril", "Promptwares", jobType);
            var programPath = Path.Combine(promptwareDir, "Program.md");
            Assert.True(File.Exists(programPath), $"Expected {programPath} to exist for job type {jobType}.");
            files.Add((RelativePathOf(repoRoot, programPath), File.ReadAllText(programPath)));

            var toolsDir = Path.Combine(promptwareDir, "Tools");
            if (!Directory.Exists(toolsDir))
            {
                continue;
            }

            foreach (var toolFile in Directory.EnumerateFiles(toolsDir, "*", SearchOption.AllDirectories))
            {
                files.Add((RelativePathOf(repoRoot, toolFile), File.ReadAllText(toolFile)));
            }
        }

        return files;
    }

    private static string RelativePathOf(string repoRoot, string path) =>
        Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}
