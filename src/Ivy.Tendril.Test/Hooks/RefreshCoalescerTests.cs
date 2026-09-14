using System.Reactive.Linq;
using Ivy.Core.Hooks;
using Ivy.Tendril.Hooks;
using Microsoft.Reactive.Testing;

namespace Ivy.Tendril.Test.Hooks;

public class RefreshCoalescerTests
{
    // RefreshToken wraps an IState<(Guid, object?, bool)> and has no test-friendly constructor,
    // so we back it with a real State<T> and count emissions after the initial replayed value.
    private static (RefreshToken Token, Func<int> RefreshCount) CreateRefreshToken()
    {
        var state = new State<(Guid, object?, bool)>((Guid.NewGuid(), null, false));
        var count = 0;
        state.Skip(1).Subscribe(_ => count++);
        return (new RefreshToken(state), () => count);
    }

    [Fact]
    public void FiveRequestsInOneWindow_ProduceExactlyOneRefresh()
    {
        var scheduler = new TestScheduler();
        var (token, refreshCount) = CreateRefreshToken();
        using var coalescer = new RefreshCoalescer(token, TimeSpan.FromMilliseconds(400), scheduler);

        for (var i = 0; i < 5; i++)
        {
            coalescer.Request();
            scheduler.AdvanceBy(TimeSpan.FromMilliseconds(50).Ticks);
        }

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(400).Ticks);

        Assert.Equal(1, refreshCount());
    }

    [Fact]
    public void RequestsInSuccessiveWindows_ProduceOneRefreshEach()
    {
        var scheduler = new TestScheduler();
        var (token, refreshCount) = CreateRefreshToken();
        using var coalescer = new RefreshCoalescer(token, TimeSpan.FromMilliseconds(400), scheduler);

        coalescer.Request();
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(400).Ticks);
        Assert.Equal(1, refreshCount());

        coalescer.Request();
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(400).Ticks);
        Assert.Equal(2, refreshCount());
    }

    [Fact]
    public void RequestAfterDispose_IsIgnoredRatherThanThrowing()
    {
        // A PlansChanged handler for a view that is being torn down can still be invoked after
        // the view's coalescer is disposed: the raiser snapshots its invocation list before the
        // unsubscribe runs, and raises on a timer thread. Throwing here landed in that raiser's
        // catch as "PlansChanged subscriber threw", which is a net meant for real bugs.
        var scheduler = new TestScheduler();
        var (token, refreshCount) = CreateRefreshToken();
        var coalescer = new RefreshCoalescer(token, TimeSpan.FromMilliseconds(400), scheduler);

        coalescer.Dispose();

        coalescer.Request();
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(400).Ticks);

        Assert.Equal(0, refreshCount());
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var scheduler = new TestScheduler();
        var (token, _) = CreateRefreshToken();
        var coalescer = new RefreshCoalescer(token, TimeSpan.FromMilliseconds(400), scheduler);

        coalescer.Dispose();
        coalescer.Dispose();
    }

    [Fact]
    public void DisposeBeforeWindowElapses_ProducesNoRefresh()
    {
        var scheduler = new TestScheduler();
        var (token, refreshCount) = CreateRefreshToken();
        var coalescer = new RefreshCoalescer(token, TimeSpan.FromMilliseconds(400), scheduler);

        coalescer.Request();
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
        coalescer.Dispose();
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(400).Ticks);

        Assert.Equal(0, refreshCount());
    }
}
