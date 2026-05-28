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

    private readonly IConfigService _config;

    public BugReportService(IConfigService config) => _config = config;

    public record BugReportFile(string AbsolutePath, string ZipEntryPath);
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
            zip.CreateEntryFromFile(file.AbsolutePath, file.ZipEntryPath.Replace('\\', '/'));

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
