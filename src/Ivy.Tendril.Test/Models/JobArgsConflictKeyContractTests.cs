using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Models;

/// <summary>
///     The contract behind #2710: deduplication used to be an allow-list of five job-type names in
///     <c>JobService.TryRejectConflictingJob</c>, so CreatePlan, CreatePr, CreateIssue and SetupProject
///     were simply missing from it and shipped undeduplicated. <see cref="JobArgsBase.ConflictKey" />
///     being abstract forces every subtype to state an answer, and this table forces that answer to be
///     a deliberate one: adding a type without deciding fails here by name.
/// </summary>
public class JobArgsConflictKeyContractTests
{
    private const string SampleFolder = "/plans/00601-SamplePlan";

    /// <summary>
    ///     Every concrete <see cref="JobArgsBase" /> subtype mapped to whether a representative instance
    ///     must produce a non-null <see cref="JobArgsBase.ConflictKey" />. False means an intentional
    ///     opt-out, documented on the type itself, not an omission.
    /// </summary>
    private static readonly Dictionary<Type, bool> Expectations = new()
    {
        [typeof(CreatePlanArgs)] = true,
        [typeof(ExecutePlanArgs)] = true,
        [typeof(RetryPlanArgs)] = true,
        [typeof(ExpandPlanArgs)] = true,
        [typeof(UpdatePlanArgs)] = true,
        [typeof(SplitPlanArgs)] = true,
        [typeof(CreatePrArgs)] = true,
        [typeof(CreateIssueArgs)] = true,
        [typeof(SetupProjectArgs)] = true,
        [typeof(SyncRepoArgs)] = false,
        [typeof(AddProjectArgs)] = false
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
            "Add each to Expectations (true if ConflictKey should be non-null, false for a documented opt-out).");

        var stale = Expectations.Keys.Except(reflected).Select(t => t.Name).ToList();
        Assert.True(stale.Count == 0,
            $"Expectations names type(s) that no longer exist: {string.Join(", ", stale)}");

        foreach (var (type, expectsKey) in Expectations)
        {
            var conflictKey = CreateSample(type).ConflictKey;
            if (expectsKey)
                Assert.False(string.IsNullOrEmpty(conflictKey),
                    $"{type.Name} is expected to be deduplicated but produced no ConflictKey");
            else
                Assert.True(conflictKey == null,
                    $"{type.Name} is a documented dedup opt-out but produced ConflictKey '{conflictKey}'");
        }
    }

    [Fact]
    public void ConflictKey_MatchesPlanFolder_ForEveryPlanScopedType()
    {
        // CreatePlan is the one deduplicated type with no plan to scope to, so it is excluded here and
        // covered below.
        var planScoped = Expectations
            .Where(e => e.Value && e.Key != typeof(CreatePlanArgs))
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
    public void ForceDuplicate_IsFalseByDefault_AndTrueOnlyWhenForceIsSet()
    {
        // Virtual with a false default, so a type with no --force affordance cannot opt out of dedup.
        Assert.All(ConcreteJobArgsTypes(), t => Assert.False(CreateSample(t).ForceDuplicate));

        Assert.True(new CreatePlanArgs("Add a widget", "Tendril", Force: true).ForceDuplicate);
        Assert.True(new CreatePrArgs(SampleFolder, Force: true).ForceDuplicate);
        Assert.True(new CreateIssueArgs(SampleFolder, "repo", Force: true).ForceDuplicate);
    }
}
