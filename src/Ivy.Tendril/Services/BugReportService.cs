using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Services;

public sealed class BugReportService
{
    private const string BugReportApiUrl = "https://tendril-api.ivy.app/report-bug";
    private const string RedactedPlaceholder = "[REDACTED]";

    private readonly IConfigService _config;

    public BugReportService(IConfigService config) => _config = config;

    /// <summary>
    ///     A file to include in a bug report. When <see cref="Content" /> is set the bytes are zipped directly
    ///     (used for synthesized artifacts like the sanitized config); otherwise the file is read from <see cref="AbsolutePath" />.
    /// </summary>
    public record BugReportFile(string AbsolutePath, string ZipEntryPath, byte[]? Content = null);
    public record BugReportResult(string ReportId, string IssueUrl);

    public static string NormalizeJobId(string input) => JobId.Normalize(input);

    public List<BugReportFile> CollectFilesForJob(string jobId)
    {
        var files = new List<BugReportFile>();
        var normalized = NormalizeJobId(jobId);
        CollectJobFiles([normalized], files);

        // Make the job report self-sufficient for plan-context debugging: include the parent plan's
        // plan.yaml and revisions plus a synthesized identity for each worktree. Worktree trees
        // themselves stay excluded for size.
        var planFolder = ResolvePlanFolderForJob(normalized);
        if (planFolder != null)
        {
            CollectPlanFiles(planFolder, files);
            var worktrees = BuildWorktreesManifest(planFolder);
            if (worktrees != null)
                files.Add(worktrees);
        }

        AddSanitizedConfig(files);
        return files;
    }

    /// <summary>
    ///     Finds the plan folder that owns <paramref name="normalizedJobId" />. Plan-connected jobs are named
    ///     <c>&lt;jobId&gt;-&lt;planId&gt;-&lt;type&gt;</c>, so the plan id is read straight off the filename.
    ///     A CreatePlan job's stem has no plan id, so fall back to the <c>PlanId</c> line its log records.
    ///     Returns <c>null</c> when the job produced no plan or none can be located.
    /// </summary>
    private string? ResolvePlanFolderForJob(string normalizedJobId)
    {
        var plansRoot = _config.PlanFolder;
        if (string.IsNullOrEmpty(plansRoot) || !Directory.Exists(plansRoot))
            return null;

        var artifacts = JobLogPaths.AllForJobId(_config.TendrilHome, normalizedJobId);

        var planId = artifacts
                         .Select(f => PlanIdFromStem(Path.GetFileName(f)))
                         .FirstOrDefault(id => id != null)
                     ?? artifacts
                         .Where(IsJobLog)
                         .Select(PlanIdFromJobLog)
                         .FirstOrDefault(id => id != null);
        if (planId == null) return null;

        return Directory.GetDirectories(plansRoot, $"{planId}-*").FirstOrDefault();
    }

    private static bool IsJobLog(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     The plan id in a <c>&lt;jobId&gt;-&lt;planId&gt;-&lt;type&gt;.*</c> artifact name, or <c>null</c>
    ///     for the plan-less <c>&lt;jobId&gt;-&lt;type&gt;.*</c> form.
    /// </summary>
    private static string? PlanIdFromStem(string fileName)
    {
        var parts = fileName.Split('-');
        return parts.Length >= 3 && parts[1].Length == 5 && parts[1].All(char.IsDigit)
            ? parts[1]
            : null;
    }

    /// <summary>The <c>- **PlanId:** 00075</c> line written into every Job Log by <c>JobLogWriter</c>.</summary>
    private static string? PlanIdFromJobLog(string jobLogPath)
    {
        try
        {
            var match = PlanIdLineRegex.Match(File.ReadAllText(jobLogPath));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static readonly Regex PlanIdLineRegex =
        new(@"^-\s*\*\*PlanId:\*\*\s*(\d{5})\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    ///     Builds <c>worktrees.txt</c>: for each <c>Worktrees/&lt;dir&gt;</c> under the plan, the git remote
    ///     origin url, the HEAD branch, and the HEAD sha. This is the identity needed to spot a project/repo
    ///     mismatch (e.g. plan project vs. the repo the worktree actually points at) without bundling the trees.
    ///     Returns <c>null</c> when the plan has no worktrees.
    /// </summary>
    private static BugReportFile? BuildWorktreesManifest(string planFolder)
    {
        var worktreesDir = Path.Combine(planFolder, "Worktrees");
        if (!Directory.Exists(worktreesDir))
            return null;

        var dirs = Directory.GetDirectories(worktreesDir);
        if (dirs.Length == 0)
            return null;

        var sb = new System.Text.StringBuilder();
        foreach (var wtDir in dirs)
        {
            var remote = GitField(wtDir, "remote get-url origin");
            var branch = GitField(wtDir, "rev-parse --abbrev-ref HEAD");
            var sha = GitField(wtDir, "rev-parse HEAD");

            sb.AppendLine(Path.GetFileName(wtDir));
            sb.AppendLine($"  remote: {remote}");
            sb.AppendLine($"  branch: {branch}");
            sb.AppendLine($"  HEAD:   {sha}");
            sb.AppendLine();
        }

        return new BugReportFile(string.Empty, "worktrees.txt", System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string GitField(string workingDir, string args) =>
        GitHelper.RunGitCapture(workingDir, args, 5000)?.Trim() is { Length: > 0 } value
            ? value
            : "(unknown)";

    public List<BugReportFile> CollectFilesForPlan(string planFolder)
    {
        var files = new List<BugReportFile>();

        CollectPlanFiles(planFolder, files);

        var jobIds = JobIdsForPlan(planFolder);
        if (jobIds.Count > 0)
            CollectJobFiles(jobIds, files);

        AddSanitizedConfig(files);

        return files;
    }

    /// <summary>
    ///     Every job that touched <paramref name="planFolder" />. Plan-connected jobs carry the plan id in
    ///     their filename. The CreatePlan job that produced the plan does not — its stem is
    ///     <c>&lt;jobId&gt;-CreatePlan</c> — so it is recovered from the <c>PlanId</c> line in its log.
    ///     Without that second pass a plan's bug report would omit the job that authored the plan.
    /// </summary>
    private List<string> JobIdsForPlan(string planFolder)
    {
        var planId = JobLogPaths.PlanIdFromFolderName(Path.GetFileName(planFolder.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        if (planId == null) return [];

        var jobIds = JobLogPaths.LogsForPlanId(_config.TendrilHome, planId)
            .Select(f => Path.GetFileName(f).Split('-')[0])
            .ToList();

        jobIds.AddRange(JobIdsOfPlanlessJobsNaming(planId));

        return jobIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Job ids whose stem has no plan id but whose log names <paramref name="planId" />.</summary>
    private IEnumerable<string> JobIdsOfPlanlessJobsNaming(string planId)
    {
        var jobsDir = JobLogPaths.JobsDir(_config.TendrilHome);
        if (!Directory.Exists(jobsDir)) yield break;

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(jobsDir, "*.md");
        }
        catch
        {
            yield break;
        }

        foreach (var file in candidates)
        {
            if (!IsJobLog(file)) continue;
            var name = Path.GetFileName(file);
            if (PlanIdFromStem(name) != null) continue;   // already covered by the filename glob
            if (PlanIdFromJobLog(file) != planId) continue;

            var dash = name.IndexOf('-');
            if (dash > 0) yield return name[..dash];
        }
    }

    public async Task<BugReportResult?> SubmitReportAsync(string description, IReadOnlyList<BugReportFile> files, string? githubUser = null, CancellationToken ct = default)
    {
        var zipPath = CreateZip(files);
        try
        {
            var version = typeof(BugReportService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            var osVersion = Environment.OSVersion.VersionString;
            var agent = _config.Settings.CodingAgent;
            var commitId = GetCommitId();

            return await UploadAsync(zipPath, description, osVersion, version, agent, commitId, githubUser, ct);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    /// <summary>
    ///     Reads the git commit the assembly was built from, embedded as <c>[AssemblyMetadata("CommitId", ...)]</c>
    ///     by the <c>SetCommitId</c> MSBuild target. Returns <c>null</c> when no commit was embedded
    ///     (e.g. a build with no git working tree), in which case the report is sent without a commit id.
    /// </summary>
    private static string? GetCommitId() =>
        typeof(BugReportService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "CommitId")?.Value;

    private void AddSanitizedConfig(List<BugReportFile> files)
    {
        var config = CollectSanitizedConfig();
        if (config != null)
            files.Add(config);
    }

    /// <summary>
    ///     Reads <c>config.yaml</c>, strips secret-bearing fields (auth password/hash, API keys, agent environment
    ///     variable values) and returns it as an in-memory file. Bug reports become public GitHub issues, so the raw
    ///     config (which may contain credentials) must never be uploaded verbatim. Reads from disk rather than the
    ///     in-memory <c>Settings</c> so that <c>%VAR%</c> references are kept literal instead of expanded to their values.
    ///     Returns <c>null</c> when there is no config or it cannot be parsed — failing closed rather than risk a leak.
    /// </summary>
    public BugReportFile? CollectSanitizedConfig()
    {
        var configPath = _config.ConfigPath;
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            return null;

        string yaml;
        try
        {
            var raw = FileHelper.ReadAllText(configPath);
            raw = QuoteUnquotedVariablePatterns(raw);
            var settings = YamlHelper.Deserializer.Deserialize<TendrilSettings>(raw) ?? new TendrilSettings();
            RedactSecrets(settings);
            yaml = YamlHelper.SerializerCompact.Serialize(settings);
        }
        catch
        {
            return null;
        }

        return new BugReportFile(string.Empty, "config.sanitized.yaml", System.Text.Encoding.UTF8.GetBytes(yaml));
    }

    private static void RedactSecrets(TendrilSettings settings)
    {
        if (settings.Auth != null)
        {
            if (!string.IsNullOrEmpty(settings.Auth.Password))
                settings.Auth.Password = RedactedPlaceholder;
            if (!string.IsNullOrEmpty(settings.Auth.HashSecret))
                settings.Auth.HashSecret = RedactedPlaceholder;
        }

        if (settings.Llm != null && !string.IsNullOrEmpty(settings.Llm.ApiKey))
            settings.Llm.ApiKey = RedactedPlaceholder;

        if (settings.Api != null && !string.IsNullOrEmpty(settings.Api.ApiKey))
            settings.Api.ApiKey = RedactedPlaceholder;

        foreach (var agent in settings.CodingAgents)
            foreach (var key in agent.EnvironmentVariables.Keys.ToList())
                agent.EnvironmentVariables[key] = RedactedPlaceholder;
    }

    // Mirrors ConfigService's preprocessing so unquoted %VAR% values that the app can load also parse here.
    private static string QuoteUnquotedVariablePatterns(string yaml)
    {
        yaml = Regex.Replace(yaml, @"(?m)(?<=:\s+)(%\w+%.*)$", "'$1'");
        yaml = Regex.Replace(yaml, @"(?m)^(\s*-\s+)(%\w+%.*)$", "$1'$2'");
        return yaml;
    }

    /// <summary>
    ///     Collects every artifact each job produced — log, prompt, raw CLI output and eventwire stream.
    /// </summary>
    private void CollectJobFiles(IReadOnlyCollection<string> jobIds, List<BugReportFile> files)
    {
        foreach (var jobId in jobIds)
        foreach (var file in JobLogPaths.AllForJobId(_config.TendrilHome, jobId))
            files.Add(new BugReportFile(file, Path.Combine("Jobs", Path.GetFileName(file))));
    }

    private static void CollectPlanFiles(string planFolder, List<BugReportFile> files)
    {
        if (!Directory.Exists(planFolder))
            return;

        CollectPlanFilesRecursive(planFolder, planFolder, files);
    }

    private static void CollectPlanFilesRecursive(string rootFolder, string currentFolder, List<BugReportFile> files)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(currentFolder))
            {
                try
                {
                    var relativePath = Path.GetRelativePath(rootFolder, file);
                    if (relativePath.StartsWith("Worktrees", StringComparison.OrdinalIgnoreCase))
                        continue;

                    files.Add(new BugReportFile(file, relativePath));
                }
                catch
                {
                    // Skip files that fail relative path resolution or cause minor path errors
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(currentFolder))
            {
                try
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.Equals("Worktrees", StringComparison.OrdinalIgnoreCase))
                        continue;

                    CollectPlanFilesRecursive(rootFolder, dir, files);
                }
                catch
                {
                    // Skip unreadable directories gracefully instead of crashing
                }
            }
        }
        catch
        {
            // Skip directory enumeration failures gracefully
        }
    }

    private static string CreateZip(IReadOnlyList<BugReportFile> files)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"tendril-bug-report-{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        foreach (var file in files)
        {
            var entryPath = file.ZipEntryPath.Replace('\\', '/');
            if (file.Content != null)
            {
                var entry = zip.CreateEntry(entryPath);
                using var entryStream = entry.Open();
                entryStream.Write(file.Content, 0, file.Content.Length);
            }
            else
            {
                zip.CreateEntryFromFile(file.AbsolutePath, entryPath);
            }
        }

        return zipPath;
    }

    private static async Task<BugReportResult?> UploadAsync(string zipPath, string description, string osVersion, string tendrilVersion, string agent, string? commitId, string? githubUser, CancellationToken ct)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        using var form = new MultipartFormDataContent();

        form.Add(new StringContent(description), "description");
        form.Add(new StringContent(osVersion), "osVersion");
        form.Add(new StringContent(tendrilVersion), "tendrilVersion");
        form.Add(new StringContent(agent ?? string.Empty), "agent");

        if (!string.IsNullOrWhiteSpace(commitId))
            form.Add(new StringContent(commitId), "commitId");

        if (!string.IsNullOrWhiteSpace(githubUser))
            form.Add(new StringContent(githubUser.Trim()), "githubUser");

        var fileStream = File.OpenRead(zipPath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(fileContent, "file", "bug-report.zip");

        var response = await httpClient.PostAsync(BugReportApiUrl, form, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Bug report upload failed with status code {response.StatusCode}. Details: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<BugReportResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
