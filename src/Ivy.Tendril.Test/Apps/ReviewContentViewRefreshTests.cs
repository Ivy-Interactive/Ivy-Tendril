using ReviewContentView = Ivy.Tendril.Apps.Review.ContentView;

namespace Ivy.Tendril.Test.Apps;

/// <summary>
///     The Review content view rebuilt itself for every <c>PlansChanged</c>, whichever plan it named. A
///     rebuild shells out to git and evaluates the project's review-action conditions in PowerShell, so a
///     burst of plan closes meant one of those per close for a plan nobody was looking at (#2571).
/// </summary>
public class ReviewContentViewRefreshTests
{
    [Fact]
    public void ShouldRefreshFor_UnrelatedFolder_IsIgnored()
    {
        Assert.False(ReviewContentView.ShouldRefreshFor("00408-StopTheFreezing", "00420-SomethingElse"));
        Assert.False(ReviewContentView.ShouldRefreshFor(
            @"D:\.tendril\Plans\00408-StopTheFreezing", "00420-SomethingElse"));
    }

    [Fact]
    public void ShouldRefreshFor_NullFolder_Refreshes()
    {
        // A null folder is a full rescan: anything may have changed, including the selected plan.
        Assert.True(ReviewContentView.ShouldRefreshFor(null, "00420-SomethingElse"));
        Assert.True(ReviewContentView.ShouldRefreshFor("", "00420-SomethingElse"));
    }

    [Fact]
    public void ShouldRefreshFor_NothingSelected_Refreshes()
    {
        Assert.True(ReviewContentView.ShouldRefreshFor("00408-StopTheFreezing", null));
        Assert.True(ReviewContentView.ShouldRefreshFor("00408-StopTheFreezing", ""));
    }

    [Fact]
    public void ShouldRefreshFor_SelectedFolder_Refreshes()
    {
        Assert.True(ReviewContentView.ShouldRefreshFor("00408-StopTheFreezing", "00408-StopTheFreezing"));
    }

    [Fact]
    public void ShouldRefreshFor_ComparesTheLastSegmentOnly()
    {
        // The watcher raises a full path; NotifyChanged callers raise a bare folder name. Both name the
        // same plan and both have to match the selected plan's folder name.
        Assert.True(ReviewContentView.ShouldRefreshFor(
            @"D:\.tendril\Plans\00408-StopTheFreezing", "00408-StopTheFreezing"));
        Assert.True(ReviewContentView.ShouldRefreshFor(
            "D:/.tendril/Plans/00408-StopTheFreezing/", "00408-StopTheFreezing"));
        Assert.True(ReviewContentView.ShouldRefreshFor(
            @"D:\.tendril\Plans\00408-StopTheFreezing", @"C:\somewhere\else\00408-StopTheFreezing"));
    }

    [Fact]
    public void ShouldRefreshFor_IsCaseInsensitive()
    {
        // Folder names round-trip through paths the user may have typed with any casing.
        Assert.True(ReviewContentView.ShouldRefreshFor("00408-stopthefreezing", "00408-StopTheFreezing"));
    }
}
