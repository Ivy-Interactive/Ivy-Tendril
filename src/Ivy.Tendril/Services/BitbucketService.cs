using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class BitbucketService : IBitbucketService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BitbucketService> _logger;

    public BitbucketService(IHttpClientFactory httpClientFactory, ILogger<BitbucketService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("Bitbucket");
        client.BaseAddress = new Uri("https://api.bitbucket.org/2.0/");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var username = Environment.GetEnvironmentVariable("BITBUCKET_USERNAME");
        var password = Environment.GetEnvironmentVariable("BITBUCKET_APP_PASSWORD");

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
        }

        return client;
    }

    public async Task<(Dictionary<string, string> statuses, string? error)> GetPrStatusesAsync(string workspace, string repoSlug, List<string> prUrls)
    {
        var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var client = CreateClient();

        foreach (var url in prUrls)
        {
            try
            {
                // Parse PR ID from URL
                // Expected format: https://bitbucket.org/{workspace}/{repo_slug}/pull-requests/{id}
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Trim('/').Split('/');
                var idIndex = Array.IndexOf(segments, "pull-requests");
                
                if (idIndex < 0 || idIndex + 1 >= segments.Length)
                    continue; // Unrecognized format

                var prIdStr = segments[idIndex + 1];
                if (!int.TryParse(prIdStr, out var prId))
                    continue;

                var endpoint = $"repositories/{workspace}/{repoSlug}/pullrequests/{prId}";
                var response = await client.GetAsync(endpoint);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("state", out var stateProp))
                    {
                        var state = stateProp.GetString();
                        var resolvedStatus = state switch
                        {
                            "OPEN" => "Open",
                            "MERGED" => "Merged",
                            "DECLINED" => "Closed",
                            "SUPERSEDED" => "Closed",
                            _ => state ?? "Open"
                        };
                        statuses[url] = resolvedStatus;
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to fetch PR {PrId} status: {StatusCode}", prId, response.StatusCode);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        return (statuses, "Unauthorized. Check your BITBUCKET_USERNAME and BITBUCKET_APP_PASSWORD environment variables.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception fetching PR status for {Url}", url);
            }
        }

        return (statuses, null);
    }

    public Task<(List<string> assignees, string? error)> GetAssigneesAsync(string workspace, string repoSlug)
    {
        // Bitbucket doesn't have a direct equivalent to repo assignees, 
        // usually you query workspace members. Stubbing for now.
        return Task.FromResult((new List<string>(), (string?)null));
    }

    public Task<(List<string> labels, string? error)> GetLabelsAsync(string workspace, string repoSlug)
    {
        // Bitbucket Cloud has no direct native equivalent of issue/PR labels at the repo level like GitHub.
        return Task.FromResult((new List<string>(), (string?)null));
    }
}
