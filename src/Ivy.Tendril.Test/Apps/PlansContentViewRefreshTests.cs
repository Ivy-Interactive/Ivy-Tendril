using System.Reactive.Linq;
using Ivy.Core.Hooks;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Services.Plans;
using Microsoft.Reactive.Testing;
using PlansContentView = Ivy.Tendril.Apps.Plans.ContentView;

namespace Ivy.Tendril.Test.Apps;

/// <summary>
///     The Plans content view never revalidated its content query at all: the query key is the selected
///     plan's folder path, so while a plan stayed open its commits, git changes and artifacts were served
///     from cache forever, including while a job was executing it. Adding the subscription reintroduces
///     the cost #2571 was about unless it is gated on the changed plan folder and coalesced, so these
///     tests assert the gate and the coalescing at the seam that drives the revalidate.
/// </summary>
public class PlansContentViewRefreshTests
{
    // RefreshToken wraps an IState<(Guid, object?, bool)> and has no test-friendly constructor, so we
    // back it with a real State<T> and count emissions after the initial replayed value. Each emission
    // is one refresh, which is what makes the view call planContentQuery.Mutator.Revalidate().
    private static (RefreshToken Token, Func<int> RefreshCount) CreateRefreshToken()
    {
        var state = new State<(Guid, object?, bool)>((Guid.NewGuid(), null, false));
        var count = 0;
        state.Skip(1).Subscribe(_ => count++);
        return (new RefreshToken(state), () => count);
    }

    [Theory]
    // A different plan is the case that must not cost anything.
    [InlineData("00408-StopTheFreezing", "00420-SomethingElse", 0)]
    [InlineData(@"D:\.tendril\Plans\00408-StopTheFreezing", "00420-SomethingElse", 0)]
    // A null or empty changed folder is a full rescan: anything may have changed, the open plan included.
    [InlineData(null, "00420-SomethingElse", 1)]
    [InlineData("", "00420-SomethingElse", 1)]
    // Nothing selected has nothing to compare against.
    [InlineData("00408-StopTheFreezing", null, 1)]
    [InlineData("00408-StopTheFreezing", "", 1)]
    // The watcher raises a full path, NotifyChanged callers raise a bare folder name, and both name the
    // plan on screen.
    [InlineData("00408-StopTheFreezing", "00408-StopTheFreezing", 1)]
    [InlineData(@"D:\.tendril\Plans\00408-StopTheFreezing", "00408-StopTheFreezing", 1)]
    [InlineData("D:/.tendril/Plans/00408-StopTheFreezing/", "00408-StopTheFreezing", 1)]
    [InlineData("00408-stopthefreezing", "00408-StopTheFreezing", 1)]
    public void PlanChangeHookDisposable_RefreshesOnlyForTheSelectedPlan(
        string? changedFolder, string? selectedFolder, int expectedRefreshes)
    {
        var scheduler = new TestScheduler();
        var watcher = new FakePlanWatcherService();
        var (token, refreshCount) = CreateRefreshToken();
        using var coalescer = new RefreshCoalescer(token, PlansContentView.PlanRefreshWindow, scheduler);

        using var hook = PlansContentView.PlanChangeHookDisposable(watcher, coalescer, () => selectedFolder);

        watcher.NotifyChanged(changedFolder);
        scheduler.AdvanceBy(PlansContentView.PlanRefreshWindow.Ticks);

        Assert.Equal(expectedRefreshes, refreshCount());
    }

    [Fact]
    public void PlanChangeHookDisposable_BurstNamingTheSelectedPlan_RefreshesOnce()
    {
        var scheduler = new TestScheduler();
        var watcher = new FakePlanWatcherService();
        var (token, refreshCount) = CreateRefreshToken();
        using var coalescer = new RefreshCoalescer(token, PlansContentView.PlanRefreshWindow, scheduler);

        using var hook = PlansContentView.PlanChangeHookDisposable(watcher, coalescer, () => "00408-StopTheFreezing");

        // One plan mutation raises several events (plan.yaml, a revision, a verification report), and a
        // rebuild shells out to git each time. Ten events inside the window must cost one.
        for (var i = 0; i < 10; i++)
        {
            watcher.NotifyChanged("00408-StopTheFreezing");
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks);
        }

        scheduler.AdvanceBy(PlansContentView.PlanRefreshWindow.Ticks);

        Assert.Equal(1, refreshCount());
    }

    [Fact]
    public void PlanChangeHookDisposable_ChangesInSuccessiveWindows_RefreshEach()
    {
        var scheduler = new TestScheduler();
        var watcher = new FakePlanWatcherService();
        var (token, refreshCount) = CreateRefreshToken();
        using var coalescer = new RefreshCoalescer(token, PlansContentView.PlanRefreshWindow, scheduler);

        using var hook = PlansContentView.PlanChangeHookDisposable(watcher, coalescer, () => "00408-StopTheFreezing");

        watcher.NotifyChanged("00408-StopTheFreezing");
        scheduler.AdvanceBy(PlansContentView.PlanRefreshWindow.Ticks);
        Assert.Equal(1, refreshCount());

        // Coalescing must not degrade into serving the cache forever, which is the defect being fixed.
        watcher.NotifyChanged("00408-StopTheFreezing");
        scheduler.AdvanceBy(PlansContentView.PlanRefreshWindow.Ticks);
        Assert.Equal(2, refreshCount());
    }

    [Fact]
    public void PlanChangeHookDisposable_ReadsTheSelectionPerEvent()
    {
        var scheduler = new TestScheduler();
        var watcher = new FakePlanWatcherService();
        var (token, refreshCount) = CreateRefreshToken();
        using var coalescer = new RefreshCoalescer(token, PlansContentView.PlanRefreshWindow, scheduler);

        // The subscription outlives the selection, so a captured folder name would gate on whichever plan
        // was open when the view first built.
        var selected = "00408-StopTheFreezing";
        using var hook = PlansContentView.PlanChangeHookDisposable(watcher, coalescer, () => selected);

        selected = "00420-SomethingElse";
        watcher.NotifyChanged("00408-StopTheFreezing");
        scheduler.AdvanceBy(PlansContentView.PlanRefreshWindow.Ticks);
        Assert.Equal(0, refreshCount());

        watcher.NotifyChanged("00420-SomethingElse");
        scheduler.AdvanceBy(PlansContentView.PlanRefreshWindow.Ticks);
        Assert.Equal(1, refreshCount());
    }

    [Fact]
    public void PlanChangeHookDisposable_Dispose_UnsubscribesPlansChanged()
    {
        var scheduler = new TestScheduler();
        var watcher = new FakePlanWatcherService();
        var (token, refreshCount) = CreateRefreshToken();
        using var coalescer = new RefreshCoalescer(token, PlansContentView.PlanRefreshWindow, scheduler);

        var hook = PlansContentView.PlanChangeHookDisposable(watcher, coalescer, () => "00408-StopTheFreezing");
        watcher.NotifyChanged("00408-StopTheFreezing");
        scheduler.AdvanceBy(PlansContentView.PlanRefreshWindow.Ticks);
        Assert.Equal(1, refreshCount());

        hook.Dispose();
        watcher.NotifyChanged("00408-StopTheFreezing");
        scheduler.AdvanceBy(PlansContentView.PlanRefreshWindow.Ticks);

        Assert.Equal(1, refreshCount());
        Assert.False(watcher.HasSubscribers);
    }

    [Fact]
    public void PlanChangeHookDisposable_RefreshTokenOverload_DisposesItsOwnCoalescer()
    {
        var watcher = new FakePlanWatcherService();
        var (token, refreshCount) = CreateRefreshToken();

        // The overload the view uses owns the coalescer, so disposing what UseEffect returns has to take
        // both the subscription and the coalescer's timer with it.
        var hook = PlansContentView.PlanChangeHookDisposable(watcher, token, () => "00408-StopTheFreezing");
        hook.Dispose();

        watcher.NotifyChanged("00408-StopTheFreezing");
        Thread.Sleep(PlansContentView.PlanRefreshWindow + TimeSpan.FromMilliseconds(200));

        Assert.Equal(0, refreshCount());
        Assert.False(watcher.HasSubscribers);
    }

    private sealed class FakePlanWatcherService : IPlanWatcherService
    {
        public event Action<string?>? PlansChanged;

        public bool HasSubscribers => PlansChanged != null;

        public void NotifyChanged(string? changedPlanFolder = null) => PlansChanged?.Invoke(changedPlanFolder);

        public void Dispose()
        {
        }
    }

    // A revalidation (loading: true, hasLoadedContent: true) keeping the last-known-good content mounted
    // is the regression #2650 is about: without it the placeholder swaps in a different widget, unmounting
    // PlanMarkdown's scroll box and throwing the reader back to the top of the plan.
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShouldShowLoadingPlaceholder_OnlyForAFetchWithNothingYetToShow(
        bool loading, bool hasLoadedContent, bool expected)
    {
        Assert.Equal(expected, PlansContentView.ShouldShowLoadingPlaceholder(loading, hasLoadedContent));
    }
}
