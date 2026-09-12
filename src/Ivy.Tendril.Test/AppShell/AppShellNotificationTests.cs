using Ivy.Tendril.AppShell;
using Microsoft.Reactive.Testing;
using static Ivy.Tendril.AppShell.TendrilAppShell;

namespace Ivy.Tendril.Test.AppShell;

public class AppShellNotificationTests
{
    [Fact]
    public void ShouldShowInAppToast_DesktopWithNativeNotificationsEnabled_ReturnsFalse()
    {
        // Native notification covers it, so the in-app toast would be a duplicate.
        Assert.False(ShouldShowInAppToast(isDesktop: true, desktopNotificationsEnabled: true));
    }

    [Fact]
    public void ShouldShowInAppToast_DesktopWithNativeNotificationsDisabled_ReturnsTrue()
    {
        // Native path is suppressed by the setting, so the toast is the only notification left.
        Assert.True(ShouldShowInAppToast(isDesktop: true, desktopNotificationsEnabled: false));
    }

    [Fact]
    public void ShouldShowInAppToast_WebWithNativeNotificationsEnabled_ReturnsTrue()
    {
        // Web mode has no native notification path, so it must always toast.
        Assert.True(ShouldShowInAppToast(isDesktop: false, desktopNotificationsEnabled: true));
    }

    [Fact]
    public void ShouldShowInAppToast_WebWithNativeNotificationsDisabled_ReturnsTrue()
    {
        // The setting only governs the native path; it must not disable the web toast.
        Assert.True(ShouldShowInAppToast(isDesktop: false, desktopNotificationsEnabled: false));
    }

    private static JobNotification Finished(string plan) =>
        new("ExecutePlan Completed", plan, true);

    private static JobNotification Failed(string plan) =>
        new("ExecutePlan Failed", $"{plan}: verification failed", false);

    [Fact]
    public void Summarize_FewNotifications_AreShownAsTheyArrived()
    {
        var batch = new List<JobNotification> { Finished("00001-A"), Failed("00002-B"), Finished("00003-C") };

        Assert.Equal(batch, NotificationBurstSummarizer.Summarize(batch));
    }

    [Fact]
    public void Summarize_BurstOfCompletions_CollapsesToOneToast()
    {
        var batch = Enumerable.Range(1, 5).Select(i => Finished($"0000{i}-Plan")).ToList();

        var summarized = NotificationBurstSummarizer.Summarize(batch);

        var summary = Assert.Single(summarized);
        Assert.Equal(NotificationBurstSummarizer.SummaryTitle, summary.Title);
        Assert.Equal("5 jobs finished", summary.Message);
        Assert.True(summary.IsSuccess);
    }

    [Fact]
    public void Summarize_BurstWithAFailure_KeepsTheFailureVisible()
    {
        var batch = Enumerable.Range(1, 6).Select(i => Finished($"0000{i}-Plan")).ToList();
        batch.Add(Failed("00007-Broken"));

        var summarized = NotificationBurstSummarizer.Summarize(batch);

        Assert.Equal(2, summarized.Count);
        Assert.Equal("6 jobs finished, 1 failed", summarized[0].Message);
        // The failure keeps its own toast: which plan failed, and why, is what the user acts on.
        Assert.Equal("00007-Broken: verification failed", summarized[1].Message);
        Assert.False(summarized[1].IsSuccess);
    }

    [Fact]
    public void Summarize_BurstOfFailures_AddsNoSummary()
    {
        var batch = Enumerable.Range(1, 4).Select(i => Failed($"0000{i}-Plan")).ToList();

        var summarized = NotificationBurstSummarizer.Summarize(batch);

        Assert.Equal(batch, summarized);
    }

    [Fact]
    public void Summarize_SingleCompletionAmongFailures_ReadsAsOneJob()
    {
        var batch = new List<JobNotification>
        {
            Failed("00001-A"), Failed("00002-B"), Failed("00003-C"), Finished("00004-D")
        };

        var summarized = NotificationBurstSummarizer.Summarize(batch);

        Assert.Equal("1 job finished, 3 failed", summarized[0].Message);
    }

    [Fact]
    public void Summarizer_BurstWithinTheWindow_ShowsOneToast()
    {
        var scheduler = new TestScheduler();
        var shown = new List<JobNotification>();
        var window = TimeSpan.FromMilliseconds(300);
        using var summarizer = new NotificationBurstSummarizer(shown.Add, window, scheduler);

        for (var i = 1; i <= 5; i++)
            summarizer.Add(Finished($"0000{i}-Plan"));

        // Nothing until the window closes: the burst is what is being waited for.
        Assert.Empty(shown);

        scheduler.AdvanceBy(window.Ticks);

        var summary = Assert.Single(shown);
        Assert.Equal("5 jobs finished", summary.Message);
    }

    [Fact]
    public void Summarizer_ALaterBurst_IsSummarizedOnItsOwn()
    {
        var scheduler = new TestScheduler();
        var shown = new List<JobNotification>();
        var window = TimeSpan.FromMilliseconds(300);
        using var summarizer = new NotificationBurstSummarizer(shown.Add, window, scheduler);

        for (var i = 1; i <= 4; i++)
            summarizer.Add(Finished($"0000{i}-Plan"));
        scheduler.AdvanceBy(window.Ticks);

        for (var i = 5; i <= 9; i++)
            summarizer.Add(Finished($"0000{i}-Plan"));
        scheduler.AdvanceBy(window.Ticks);

        Assert.Equal(["4 jobs finished", "5 jobs finished"], shown.Select(n => n.Message));
    }

    [Fact]
    public void Summarizer_SingleNotification_IsShownUnchanged()
    {
        var scheduler = new TestScheduler();
        var shown = new List<JobNotification>();
        var window = TimeSpan.FromMilliseconds(300);
        using var summarizer = new NotificationBurstSummarizer(shown.Add, window, scheduler);

        summarizer.Add(Failed("00001-Broken"));
        scheduler.AdvanceBy(window.Ticks);

        var only = Assert.Single(shown);
        Assert.Equal("00001-Broken: verification failed", only.Message);
    }
}
