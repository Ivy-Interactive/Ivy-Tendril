using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Services;

public sealed class BugReportService
{
    private static readonly Regex JobIdFromLogRegex = new(@"^(\d{5})-", RegexOptions.Compiled);
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

    public static string NormalizeJobId(string input)
    {
        input = input.Trim();
        if (int.TryParse(input, out var num))
            return num.ToString("D5");
        return input;
    }

    public List<BugReportFile> CollectFilesForJob(string jobId)
    {
        var files = new List<BugReportFile>();
        var normalized = NormalizeJobId(jobId);
        CollectPromptwareLogFiles([normalized], files);
        AddSanitizedConfig(files);
        return files;
    }

    public List<BugReportFile> CollectFilesForPlan(string planFolder)
    {
        var files = new List<BugReportFile>();
        var jobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectPlanFiles(planFolder, files);
        ExtractJobIdsFromPlanLogs(planFolder, jobIds);

        if (jobIds.Count > 0)
            CollectPromptwareLogFiles(jobIds, files);

        AddSanitizedConfig(files);

        return files;
    }

    public async Task<BugReportResult?> SubmitReportAsync(string description, IReadOnlyList<BugReportFile> files, CancellationToken ct = default)
    {
        var zipPath = CreateZip(files);
        try
        {
            var version = typeof(BugReportService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            var osVersion = Environment.OSVersion.VersionString;
            var agent = _config.Settings.CodingAgent;

            return await UploadAsync(zipPath, description, osVersion, version, agent, ct);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

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

    private void CollectPromptwareLogFiles(IReadOnlyCollection<string> jobIds, List<BugReportFile> files)
    {
        var promptsRoot = PromptwareHelper.ResolvePromptsRoot(_config.TendrilHome);
        if (!Directory.Exists(promptsRoot)) return;

        foreach (var pwDir in Directory.GetDirectories(promptsRoot))
        {
            var logsDir = Path.Combine(pwDir, "Logs");
            if (!Directory.Exists(logsDir)) continue;

            var pwName = Path.GetFileName(pwDir);

            foreach (var jobId in jobIds)
            {
                var mdFile = Path.Combine(logsDir, $"{jobId}.md");
                if (File.Exists(mdFile))
                    files.Add(new BugReportFile(mdFile, Path.Combine("Jobs", pwName, $"{jobId}.md")));

                var jsonlFile = Path.Combine(logsDir, $"{jobId}.raw.jsonl");
                if (File.Exists(jsonlFile))
                    files.Add(new BugReportFile(jsonlFile, Path.Combine("Jobs", pwName, $"{jobId}.raw.jsonl")));
            }
        }
    }

    private static void CollectPlanFiles(string planFolder, List<BugReportFile> files)
    {
        foreach (var file in Directory.EnumerateFiles(planFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(planFolder, file);
            if (relativePath.StartsWith("Worktrees", StringComparison.OrdinalIgnoreCase))
                continue;
            files.Add(new BugReportFile(file, relativePath));
        }
    }

    private static void ExtractJobIdsFromPlanLogs(string planFolder, HashSet<string> jobIds)
    {
        var logsDir = Path.Combine(planFolder, "Logs");
        if (!Directory.Exists(logsDir)) return;

        foreach (var logFile in Directory.GetFiles(logsDir, "*.md"))
        {
            var fileName = Path.GetFileNameWithoutExtension(logFile);
            var match = JobIdFromLogRegex.Match(fileName);
            if (match.Success)
                jobIds.Add(match.Groups[1].Value);
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

    private static async Task<BugReportResult?> UploadAsync(string zipPath, string description, string osVersion, string tendrilVersion, string agent, CancellationToken ct)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var form = new MultipartFormDataContent();

        form.Add(new StringContent(description), "description");
        form.Add(new StringContent(osVersion), "osVersion");
        form.Add(new StringContent(tendrilVersion), "tendrilVersion");
        form.Add(new StringContent(agent), "agent");

        var fileStream = File.OpenRead(zipPath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(fileContent, "file", "bug-report.zip");

        var response = await httpClient.PostAsync(BugReportApiUrl, form, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<BugReportResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
