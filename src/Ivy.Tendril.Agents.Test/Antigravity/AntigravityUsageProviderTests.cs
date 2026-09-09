using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;

namespace Ivy.Tendril.Agents.Test.Antigravity;

public class AntigravityUsageProviderTests
{
    private const string StandardTwoGroupFixture = """
    {"status":"SUCCESS","command":{"name":"usage","data":{"groups":[
      {"name":"Gemini Models","buckets":[
        {"id":"gemini-weekly","name":"Weekly Limit Remaining","window":"weekly",
         "remaining_fraction":0.7309908270835876,"reset_time":"2026-09-11T05:39:20Z"},
        {"id":"gemini-5h","name":"Five Hour Limit Remaining","window":"5h",
         "remaining_fraction":0.985869824886322,"reset_time":"2026-09-09T15:06:03Z"}]},
      {"name":"Claude and GPT models","buckets":[
        {"id":"3p-weekly","name":"Weekly Limit Remaining","window":"weekly",
         "remaining_fraction":1,"reset_time":"2026-09-16T10:59:26Z"},
        {"id":"3p-5h","name":"Five Hour Limit Remaining","window":"5h",
         "remaining_fraction":1,"reset_time":"2026-09-09T15:59:26Z"}]}
    ]}}}
    """;

    [Fact]
    public async Task TwoGroupFixture_MapsToTwoWindows_WorstOfBothGroups_TagsNoteWithLosingGroup()
    {
        var fixedTime = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);

        var provider = new AntigravityUsageProvider(
            runner: (_, _, _, _) => Task.FromResult((0, StandardTwoGroupFixture, "")),
            timeProvider: timeProvider);

        var snapshot = await provider.GetUsageAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(AgentId.Antigravity, snapshot.AgentId);
        Assert.Equal(fixedTime, snapshot.CapturedAt);
        Assert.Equal(2, snapshot.Windows.Count);

        // 5h window (300m) - Gemini is lower (0.9858 < 1.0)
        var w5h = snapshot.Windows.Single(w => w.WindowMinutes == 300);
        var expectedUsed5h = (1.0 - 0.985869824886322) * 100.0;
        Assert.NotNull(w5h.UsedPercent);
        Assert.Equal(expectedUsed5h, w5h.UsedPercent.Value, precision: 4);
        Assert.Equal(DateTimeOffset.Parse("2026-09-09T15:06:03Z"), w5h.ResetsAt);

        // Weekly window (10080m) - Gemini is lower (0.73099 < 1.0)
        var wWeekly = snapshot.Windows.Single(w => w.WindowMinutes == 10080);
        var expectedUsedWeekly = (1.0 - 0.7309908270835876) * 100.0;
        Assert.NotNull(wWeekly.UsedPercent);
        Assert.Equal(expectedUsedWeekly, wWeekly.UsedPercent.Value, precision: 4);
        Assert.Equal(DateTimeOffset.Parse("2026-09-11T05:39:20Z"), wWeekly.ResetsAt);

        // Note tags the losing group
        Assert.Equal("from Gemini Models", snapshot.Note);
    }

    [Fact]
    public async Task WindowStrings_WeeklyAnd5h_MapToCorrectMinutes()
    {
        var provider = new AntigravityUsageProvider(
            runner: (_, _, _, _) => Task.FromResult((0, StandardTwoGroupFixture, "")));

        var snapshot = await provider.GetUsageAsync();

        Assert.NotNull(snapshot);
        Assert.Contains(snapshot.Windows, w => w.WindowMinutes == 300);
        Assert.Contains(snapshot.Windows, w => w.WindowMinutes == 10080);
    }

    [Fact]
    public async Task NonZeroExitCode_EmptyStdout_MalformedJson_AllReturnNull()
    {
        // 1. Non-zero exit code
        var providerError = new AntigravityUsageProvider(
            runner: (_, _, _, _) => Task.FromResult((1, StandardTwoGroupFixture, "some error")));
        var snapError = await providerError.GetUsageAsync();
        Assert.Null(snapError);

        // 2. Empty stdout
        var providerEmpty = new AntigravityUsageProvider(
            runner: (_, _, _, _) => Task.FromResult((0, "", "")));
        var snapEmpty = await providerEmpty.GetUsageAsync();
        Assert.Null(snapEmpty);

        // 3. Malformed JSON
        var providerMalformed = new AntigravityUsageProvider(
            runner: (_, _, _, _) => Task.FromResult((0, "{ not valid json", "")));
        var snapMalformed = await providerMalformed.GetUsageAsync();
        Assert.Null(snapMalformed);
    }

    [Fact]
    public async Task CapturedAt_IsSetToCallTime_NotParsedFromResponse()
    {
        var callTime = new DateTimeOffset(2026, 9, 9, 14, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(callTime);

        var provider = new AntigravityUsageProvider(
            runner: (_, _, _, _) => Task.FromResult((0, StandardTwoGroupFixture, "")),
            timeProvider: timeProvider);

        var snapshot = await provider.GetUsageAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(callTime, snapshot.CapturedAt);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
