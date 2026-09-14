using System.Net;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;

namespace Ivy.Tendril.Agents.Test.Claude;

public class ClaudeUsageProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 7, 30, 0, TimeSpan.Zero);

    private const string StandardFixture = """
    {"five_hour":{"utilization":6.0,"resets_at":"2026-09-14T09:00:00.748893+00:00","locked_reason":null},
     "seven_day":{"utilization":12.5,"resets_at":"2026-09-19T19:00:00.748921+00:00","locked_reason":null},
     "seven_day_opus":null,"seven_day_sonnet":null,
     "extra_usage":{"is_enabled":false,"utilization":0.0}}
    """;

    private readonly string _tempDir;
    private readonly string _credentialsPath;

    public ClaudeUsageProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claude_usage_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _credentialsPath = Path.Combine(_tempDir, ".credentials.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task ParsesFiveHourAndWeekly_IntoTwoWindows_WithOAuthHeaders()
    {
        WriteCredentials("token-abc", Now.AddHours(1));
        HttpRequestMessage? captured = null;
        var provider = CreateProvider(request =>
        {
            captured = request;
            return JsonResponse(StandardFixture);
        });

        var snapshot = await provider.GetUsageAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(AgentId.Claude, snapshot.AgentId);
        Assert.Equal(Now, snapshot.CapturedAt);
        Assert.Null(snapshot.Note);
        Assert.Equal(2, snapshot.Windows.Count);

        var fiveHour = snapshot.Windows[0];
        Assert.Equal(300, fiveHour.WindowMinutes);
        Assert.Equal(6.0, fiveHour.UsedPercent);
        Assert.Equal(94.0, fiveHour.RemainingPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-14T09:00:00.748893+00:00"), fiveHour.ResetsAt);

        var weekly = snapshot.Windows[1];
        Assert.Equal(10080, weekly.WindowMinutes);
        Assert.Equal(12.5, weekly.UsedPercent);
        Assert.Equal(87.5, weekly.RemainingPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-19T19:00:00.748921+00:00"), weekly.ResetsAt);

        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("token-abc", captured.Headers.Authorization?.Parameter);
        Assert.Equal("oauth-2025-04-20", Assert.Single(captured.Headers.GetValues("anthropic-beta")));
    }

    [Fact]
    public async Task ModelScopedWeeklyMoreUsed_WinsAndTagsNote_NullBucketsSkipped()
    {
        WriteCredentials("token", Now.AddHours(1));
        var provider = CreateProvider(_ => JsonResponse("""
        {"five_hour":null,
         "seven_day":{"utilization":20.0,"resets_at":"2026-09-19T19:00:00Z"},
         "seven_day_opus":{"utilization":75.0,"resets_at":null},
         "seven_day_sonnet":{"utilization":null,"resets_at":null}}
        """));

        var snapshot = await provider.GetUsageAsync();

        Assert.NotNull(snapshot);
        var weekly = Assert.Single(snapshot.Windows);
        Assert.Equal(10080, weekly.WindowMinutes);
        Assert.Equal(75.0, weekly.UsedPercent);
        Assert.Null(weekly.ResetsAt);
        Assert.Equal("from Opus weekly limit", snapshot.Note);
    }

    [Fact]
    public async Task MissingExpiredOrTokenlessCredentials_ReturnNull_WithoutCallingEndpoint()
    {
        var calls = 0;
        var provider = CreateProvider(_ =>
        {
            calls++;
            return JsonResponse(StandardFixture);
        });

        Assert.Null(await provider.GetUsageAsync());

        WriteCredentials("token", Now.AddMinutes(-1));
        Assert.Null(await provider.GetUsageAsync());

        WriteCredentials(null, Now.AddHours(1));
        Assert.Null(await provider.GetUsageAsync());

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NonSuccessStatus_MalformedJson_AndNoWindows_AllReturnNull()
    {
        WriteCredentials("token", Now.AddHours(1));

        var unauthorized = CreateProvider(_ => JsonResponse("""{"error":"unauthorized"}""", HttpStatusCode.Unauthorized));
        Assert.Null(await unauthorized.GetUsageAsync());

        var malformed = CreateProvider(_ => JsonResponse("{ not valid json"));
        Assert.Null(await malformed.GetUsageAsync());

        var empty = CreateProvider(_ => JsonResponse("""{"five_hour":null,"seven_day":null}"""));
        Assert.Null(await empty.GetUsageAsync());
    }

    private ClaudeUsageProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => new(new HttpClient(new StubHandler(handler)), _credentialsPath, new FakeTimeProvider(Now));

    private void WriteCredentials(string? accessToken, DateTimeOffset expiresAt)
    {
        var json = JsonSerializer.Serialize(new
        {
            claudeAiOauth = new
            {
                accessToken,
                refreshToken = "refresh",
                expiresAt = expiresAt.ToUnixTimeMilliseconds(),
                subscriptionType = "max",
            },
        });
        File.WriteAllText(_credentialsPath, json);
    }

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
