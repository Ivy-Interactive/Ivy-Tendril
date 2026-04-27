using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class GithubService(IConfigService config, ILogger<GithubService> logger) : IGithubService
{
    // ConcurrentDictionary required: multiple UseQuery calls from different views/dialogs
    // can fetch different repos simultaneously, causing concurrent writes to different keys.
    private readonly ConcurrentDictionary<string, List<string>> _assigneeCache = new();
    private readonly IConfigService _config = config;
    private readonly ILogger<GithubService> _logger = logger;
    private readonly ConcurrentDictionary<string, List<string>> _labelCache = new();
    private readonly ConcurrentDictionary<string, RepoConfig?> _repoPathCache = new();
    private List<RepoConfig>? _repoCache;

    public List<RepoConfig> GetRepos()
    {
        if (_repoCache is not null)
            return _repoCache;

        var uniquePaths = _config.Settings.Projects
            .SelectMany(p => p.RepoPaths)
            .Distinct()
            .ToList();

        var repos = new List<RepoConfig>();
        foreach (var repoPath in uniquePaths)
        {
            var repoConfig = GetRepoConfigFromPathCached(repoPath);
            if (repoConfig is not null)
                repos.Add(repoConfig);
        }

        // Deduplicate by FullName (owner/name)
        _repoCache = repos
            .GroupBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return _repoCache;
    }

    public async Task<(List<string> assignees, string? error)> GetAssigneesAsync(string owner, string repo)
        => await GetCachedListAsync(_assigneeCache, owner, repo, FetchAssigneesFromGhCliAsync);

    public async Task<(List<string> labels, string? error)> GetLabelsAsync(string owner, string repo)
        => await GetCachedListAsync(_labelCache, owner, repo, FetchLabelsFromGhCliAsync);

    public async Task<(Dictionary<string, string> statuses, string? error)> GetPrStatusesAsync(string owner, string repo)
    {
        return await FetchPrStatusesFromGhCliAsync(owner, repo);
    }

    public async Task<(List<GitHubIssue> issues, string? error)> SearchIssuesAsync(IssueSearchRequest request)
    {
        try
        {
            var args =
                $"issue list --repo {request.Owner}/{request.Repo} --state open --limit 100 --json number,title,body,labels,assignees";
            if (!string.IsNullOrWhiteSpace(request.Query))
                args += $" --search \"{request.Query}\"";
            if (!string.IsNullOrWhiteSpace(request.Assignee))
                args += $" --assignee {request.Assignee}";
            if (request.Labels is { Length: > 0 })
                args += $" --label \"{string.Join(",", request.Labels)}\"";

            var psi = new ProcessStartInfo("gh", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null)
                return ([], "GitHub CLI (gh) is not available. Please install it from https://cli.github.com/");

            var output = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitOrKillAsync(60000);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("gh issue list failed for {Owner}/{Repo}: {Stderr}", request.Owner, request.Repo, stderr);
                var errorMsg = !string.IsNullOrWhiteSpace(stderr)
                    ? stderr.Trim()
                    : $"GitHub CLI exited with code {process.ExitCode}";
                return ([], errorMsg);
            }

            var issues = ParseIssuesFromJson(output);
            return (issues, null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse issues for {Owner}/{Repo}", request.Owner, request.Repo);
            return ([], "Invalid response from GitHub CLI. The output could not be parsed.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search issues for {Owner}/{Repo}", request.Owner, request.Repo);
            return ([], $"Failed to fetch issues: {ex.Message}");
        }
    }

    internal static List<GitHubIssue> ParseIssuesFromJson(string json)
    {
        var issues = new List<GitHubIssue>();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var number = element.GetProperty("number").GetInt32();
            var title = element.GetProperty("title").GetString() ?? "";
            var body = element.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;
            var issueLabels = element.GetProperty("labels").EnumerateArray()
                .Select(l => l.GetProperty("name").GetString() ?? "")
                .ToArray();
            var issueAssignees = element.GetProperty("assignees").EnumerateArray()
                .Select(a => a.GetProperty("login").GetString() ?? "")
                .ToArray();
            issues.Add(new GitHubIssue(number, title, body, issueLabels, issueAssignees));
        }

        return issues;
    }

    public RepoConfig? GetRepoConfigFromPathCached(string repoPath)
    {
        return _repoPathCache.GetOrAdd(repoPath, path => GetRepoConfigFromPath(path));
    }

    internal RepoConfig? GetRepoConfigFromPath(string repoPath)
    {
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Repository path does not exist: {RepoPath}", repoPath);
                return null;
            }

            var psi = new ProcessStartInfo("git", "remote get-url origin")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                _logger.LogWarning("Failed to start git process for {RepoPath}", repoPath);
                return null;
            }

            var url = process.StandardOutput.ReadToEnd().Trim();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExitOrKill(10000);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("git remote get-url failed for {RepoPath}: {Stderr}", repoPath, stderr);
                return null;
            }

            var config = ParseRepoConfigFromUrl(url);
            if (config is null)
            {
                _logger.LogWarning("Failed to parse remote URL for {RepoPath}: {Url}", repoPath, url);
            }
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception getting repo config for {RepoPath}", repoPath);
            return null;
        }
    }

    internal static RepoConfig? ParseRepoConfigFromUrl(string url)
    {
        var match = Regex.Match(url, @"[/:](?<owner>[^/]+)/(?<name>[^/]+?)(?:\.git)?$");
        if (!match.Success) return null;

        return new RepoConfig
        {
            Owner = match.Groups["owner"].Value,
            Name = match.Groups["name"].Value
        };
    }

    private async Task<(T result, string? error)> ExecuteGhCliAsync<T>(
        string args,
        Func<string, T> parseOutput,
        T emptyResult)
    {
        try
        {
            var psi = new ProcessStartInfo("gh", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (emptyResult, "GitHub CLI (gh) is not available. Please install it from https://cli.github.com/");

            var output = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitOrKillAsync(60000);

            if (process.ExitCode != 0)
            {
                var errorMsg = !string.IsNullOrWhiteSpace(stderr)
                    ? stderr.Trim()
                    : $"GitHub CLI exited with code {process.ExitCode}";
                return (emptyResult, errorMsg);
            }

            return (parseOutput(output), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute gh CLI: {Args}", args);
            return (emptyResult, $"Failed to execute gh CLI: {ex.Message}");
        }
    }

    private async Task<(List<string> items, string? error)> GetCachedListAsync(
        ConcurrentDictionary<string, List<string>> cache,
        string owner,
        string repo,
        Func<string, string, Task<(List<string>, string?)>> fetchFunc)
    {
        var key = $"{owner}/{repo}";
        if (cache.TryGetValue(key, out var cached))
            return (cached, null);

        var (items, error) = await fetchFunc(owner, repo);

        if (error is null)
            cache[key] = items;

        return (items, error);
    }

    private Dictionary<string, string> ParsePrStatuses(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var url = element.GetProperty("url").GetString();
            var state = element.GetProperty("state").GetString();

            if (url is not null && state is not null)
            {
                result[url] = state switch
                {
                    "OPEN" => "Open",
                    "CLOSED" => "Closed",
                    "MERGED" => "Merged",
                    _ => state
                };
            }
        }

        return result;
    }

    private async Task<(List<string> labels, string? error)> FetchLabelsFromGhCliAsync(string owner, string repo)
    {
        var (labels, error) = await ExecuteGhCliAsync(
            $"api repos/{owner}/{repo}/labels --jq \".[].name\"",
            output => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .OrderBy(x => x)
                            .ToList(),
            new List<string>());

        if (error is not null)
            _logger.LogWarning("gh api labels failed for {Owner}/{Repo}", owner, repo);

        return (labels, error);
    }

    private async Task<(Dictionary<string, string> statuses, string? error)> FetchPrStatusesFromGhCliAsync(string owner, string repo)
    {
        var (statuses, error) = await ExecuteGhCliAsync(
            $"pr list --repo {owner}/{repo} --limit 100 --state all --json url,state",
            ParsePrStatuses,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        if (error is not null)
            _logger.LogWarning("gh pr list failed for {Owner}/{Repo}", owner, repo);

        return (statuses, error);
    }

    private async Task<(List<string> assignees, string? error)> FetchAssigneesFromGhCliAsync(string owner, string repo)
    {
        var (assignees, error) = await ExecuteGhCliAsync(
            $"api repos/{owner}/{repo}/assignees --jq \".[].login\"",
            output => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .OrderBy(x => x)
                            .ToList(),
            new List<string>());

        if (error is not null)
            _logger.LogWarning("gh api assignees failed for {Owner}/{Repo}", owner, repo);

        return (assignees, error);
    }
}

public record IssueSearchRequest(
    string Owner,
    string Repo,
    string? Query = null,
    string? Assignee = null,
    string[]? Labels = null);