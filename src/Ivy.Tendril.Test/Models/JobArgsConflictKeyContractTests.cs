using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Models;

/// <summary>
///     The contract behind #2710: deduplication used to be an allow-list of five job-type names in
///     <c>JobService.TryRejectConflictingJob</c>, so CreatePlan, CreatePr, CreateIssue and SetupProject
///     were simply missing from it and shipped undeduplicated. <see cref="JobArgsBase.ConflictKey" />
///     being abstract forces every subtype to state an answer, and this table forces that answer to be
///     a deliberate one: adding a type without deciding fails here by name. These tests check the
///     group assignment, not the premise behind it; for <c>JobExclusionGroup.PlanIssue</c> that premise
///     is checked in <c>PlanIssueGroupPromptwareAuditTests</c>.
/// </summary>
public class JobArgsConflictKeyContractTests
{
    private const string SampleFolder = "/plans/00601-SamplePlan";

    /// <summary>
    ///     Every concrete <see cref="JobArgsBase" /> subtype mapped to its
    ///     <see cref="JobArgsBase.ExclusionGroup" />. <see cref="JobExclusionGroup.None" /> is the
    ///     documented dedup opt-out (a null <see cref="JobArgsBase.ConflictKey" />); anything else
    ///     expects a non-null key. This maps a type to its group <b>for the sample instance</b> below,
    ///     not universally: <see cref="SyncRepoArgs" /> is path-dependent (Plan 00675) — its sample here
    ///     is a bare checkout path, which is the documented opt-out branch, while a worktree-targeting
    ///     path takes <see cref="JobExclusionGroup.PlanWorktree" /> instead (see
    ///     <see cref="SyncRepoArgs_TakesPlanWorktreeScope_WhenItTargetsAWorktree" />).
    /// </summary>
    private static readonly Dictionary<Type, JobExclusionGroup> Expectations = new()
    {
        [typeof(CreatePlanArgs)] = JobExclusionGroup.CreatePlanTask,
        [typeof(ExecutePlanArgs)] = JobExclusionGroup.PlanWorktree,
        [typeof(RetryPlanArgs)] = JobExclusionGroup.PlanWorktree,
        [typeof(ExpandPlanArgs)] = JobExclusionGroup.PlanWorktree,
        [typeof(UpdatePlanArgs)] = JobExclusionGroup.PlanWorktree,
        [typeof(SplitPlanArgs)] = JobExclusionGroup.PlanWorktree,
        [typeof(CreatePrArgs)] = JobExclusionGroup.PlanWorktree,
        [typeof(CreateIssueArgs)] = JobExclusionGroup.PlanIssue,
        [typeof(SetupProjectArgs)] = JobExclusionGroup.ProjectSetup,
        [typeof(SyncRepoArgs)] = JobExclusionGroup.None,
        [typeof(AddProjectArgs)] = JobExclusionGroup.None
    };

    private static JobArgsBase CreateSample(Type type)
    {
        if (type == typeof(CreatePlanArgs)) return new CreatePlanArgs("Add a widget", "Tendril");
        if (type == typeof(ExecutePlanArgs)) return new ExecutePlanArgs(SampleFolder);
        if (type == typeof(RetryPlanArgs)) return new RetryPlanArgs(SampleFolder, "Try again");
        if (type == typeof(ExpandPlanArgs)) return new ExpandPlanArgs(SampleFolder);
        if (type == typeof(UpdatePlanArgs)) return new UpdatePlanArgs(SampleFolder);
        if (type == typeof(SplitPlanArgs)) return new SplitPlanArgs(SampleFolder);
        if (type == typeof(CreatePrArgs)) return new CreatePrArgs(SampleFolder);
        if (type == typeof(CreateIssueArgs)) return new CreateIssueArgs(SampleFolder, "Ivy-Interactive/ivy-tendril");
        if (type == typeof(SetupProjectArgs)) return new SetupProjectArgs(SampleFolder);
        if (type == typeof(SyncRepoArgs)) return new SyncRepoArgs("/repos/ivy-tendril", "development");
        if (type == typeof(AddProjectArgs)) return new AddProjectArgs("Tendril", [new RepoRef { Path = "/repos/ivy-tendril" }]);

        throw new InvalidOperationException(
            $"{type.Name} has no sample instance here. Add one, and add it to Expectations while you " +
            "are deciding whether it should be deduplicated.");
    }

    private static List<Type> ConcreteJobArgsTypes() =>
        typeof(JobArgsBase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(JobArgsBase).IsAssignableFrom(t))
            .OrderBy(t => t.Name)
            .ToList();

    [Fact]
    public void EveryJobArgsType_HasAnExpectationTableEntry()
    {
        var reflected = ConcreteJobArgsTypes();

        var undecided = reflected.Where(t => !Expectations.ContainsKey(t)).Select(t => t.Name).ToList();
        Assert.True(undecided.Count == 0,
            $"New JobArgsBase subtype(s) with no dedup decision recorded: {string.Join(", ", undecided)}. " +
            "Add each to Expectations (its JobExclusionGroup, or None for a documented opt-out).");

        var stale = Expectations.Keys.Except(reflected).Select(t => t.Name).ToList();
        Assert.True(stale.Count == 0,
            $"Expectations names type(s) that no longer exist: {string.Join(", ", stale)}");

        foreach (var (type, group) in Expectations)
        {
            var conflictKey = CreateSample(type).ConflictKey;
            if (group == JobExclusionGroup.None)
                Assert.True(conflictKey == null,
                    $"{type.Name} is a documented dedup opt-out but produced ConflictKey '{conflictKey}'");
            else
                Assert.False(string.IsNullOrEmpty(conflictKey),
                    $"{type.Name} is expected to be deduplicated but produced no ConflictKey");
        }
    }

    [Fact]
    public void EveryJobArgsType_DeclaresTheGroupTheGroupTableAgrees()
    {
        foreach (var (type, expectedGroup) in Expectations)
        {
            var sample = CreateSample(type);
            Assert.Equal(expectedGroup, sample.ExclusionGroup);

            if (expectedGroup == JobExclusionGroup.None)
                Assert.Empty(JobExclusionGroups.TypesIn(expectedGroup));
            else
                Assert.Contains(sample.Type, JobExclusionGroups.TypesIn(expectedGroup));
        }
    }

    [Fact]
    public void JobExclusionGroups_NamesOnlyKnownJobTypes()
    {
        var allGroups = Enum.GetValues<JobExclusionGroup>().Where(g => g != JobExclusionGroup.None);
        var seen = new Dictionary<string, JobExclusionGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in allGroups)
        {
            foreach (var name in JobExclusionGroups.TypesIn(group))
            {
                Assert.Contains(name, Constants.JobTypes.BuiltIn);

                Assert.False(seen.TryGetValue(name, out var otherGroup),
                    $"{name} appears in both {otherGroup} and {group}");
                seen[name] = group;
            }
        }
    }

    [Fact]
    public void PlanMutatingTypes_ShareOneExclusionGroup()
    {
        Type[] planMutatingTypes =
        [
            typeof(ExecutePlanArgs), typeof(RetryPlanArgs), typeof(CreatePrArgs),
            typeof(UpdatePlanArgs), typeof(ExpandPlanArgs), typeof(SplitPlanArgs)
        ];

        Assert.All(planMutatingTypes,
            t => Assert.Equal(JobExclusionGroup.PlanWorktree, CreateSample(t).ExclusionGroup));
    }

    [Fact]
    public void ConflictKey_MatchesPlanFolder_ForEveryPlanScopedType()
    {
        // CreatePlan is the one deduplicated type with no plan to scope to, so it is excluded here and
        // covered below.
        var planScoped = Expectations
            .Where(e => e.Value != JobExclusionGroup.None && e.Key != typeof(CreatePlanArgs))
            .Select(e => e.Key)
            .ToList();

        Assert.Equal(8, planScoped.Count);

        foreach (var type in planScoped)
        {
            var sample = CreateSample(type);
            Assert.Equal(SampleFolder, sample.PlanFolder);
            Assert.Equal(sample.PlanFolder, sample.ConflictKey);
        }
    }

    [Fact]
    public void ConflictKey_IsTaskHash_ForCreatePlanArgs()
    {
        var args = new CreatePlanArgs("Add a widget", "Tendril");

        // Shared with the breadcrumb naming on purpose: the two paths must not disagree about what
        // counts as the same request.
        Assert.Equal(InboxBreadcrumb.TaskHash("Tendril", "Add a widget"), args.ConflictKey);
        Assert.Contains(args.ConflictKey!, InboxBreadcrumb.FileName("Tendril", "Add a widget"));
    }

    [Fact]
    public void SyncRepoArgs_TakesPlanWorktreeScope_WhenItTargetsAWorktree()
    {
        var flat = new SyncRepoArgs(Path.Combine(SampleFolder, "Worktrees", "ivy-tendril"), "development");
        Assert.Equal(SampleFolder, flat.ConflictKey);
        Assert.Equal(JobExclusionGroup.PlanWorktree, flat.ExclusionGroup);

        var nested = new SyncRepoArgs(
            Path.Combine(SampleFolder, "Worktrees", "ivy-interactive", "ivy-tendril"), "development");
        Assert.Equal(SampleFolder, nested.ConflictKey);
        Assert.Equal(JobExclusionGroup.PlanWorktree, nested.ExclusionGroup);

        var bareCheckout = new SyncRepoArgs("/repos/ivy-tendril", "development");
        Assert.Null(bareCheckout.ConflictKey);
        Assert.Equal(JobExclusionGroup.None, bareCheckout.ExclusionGroup);
    }

    [Fact]
    public void AddProjectArgs_IsNotPlanScoped()
    {
        // AddProjectArgs.RepoRef paths belong to a project being registered, not to any plan
        // worktree — it has no plan scope at all, so it keeps the None opt-out unconditionally
        // (unlike SyncRepoArgs, whose scope depends on the path it targets).
        var args = new AddProjectArgs("Tendril", [new RepoRef { Path = "/repos/ivy-tendril" }]);

        Assert.Null(args.PlanFolder);
        Assert.Null(args.ConflictKey);
        Assert.Equal(JobExclusionGroup.None, args.ExclusionGroup);
    }

    [Fact]
    public void ForceDuplicate_IsFalseByDefault_AndTrueOnlyWhenForceIsSet()
    {
        // Virtual with a false default, so a type with no --force affordance cannot opt out of dedup.
        Assert.All(ConcreteJobArgsTypes(), t => Assert.False(CreateSample(t).ForceDuplicate));

        Assert.True(new CreatePlanArgs("Add a widget", "Tendril", Force: true).ForceDuplicate);
        Assert.True(new CreatePrArgs(SampleFolder, Force: true).ForceDuplicate);
        Assert.True(new CreateIssueArgs(SampleFolder, "repo", Force: true).ForceDuplicate);
        Assert.True(new ExecutePlanArgs(SampleFolder, Force: true).ForceDuplicate);
        Assert.True(new RetryPlanArgs(SampleFolder, "Try again", Force: true).ForceDuplicate);
    }
}
