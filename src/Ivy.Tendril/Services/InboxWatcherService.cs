using Ivy.Tendril.Helpers;
using System.Collections.Concurrent;
using Ivy.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class InboxWatcherService : IInboxWatcherService
{
    private readonly string _inboxPath;
    private readonly IJobService _jobService;
    private readonly ILogger<InboxWatcherService> _logger;
    private readonly Timer _pollTimer;
    private readonly ConcurrentDictionary<string, byte> _processing = new();
    private readonly FileSystemWatcher? _watcher;

    public InboxWatcherService(IConfigService config, IJobService jobService, ILogger<InboxWatcherService> logger)
    {
        _jobService = jobService;
        _logger = logger;
        _inboxPath = Path.Combine(config.TendrilHome, "Inbox");

        if (!Directory.Exists(_inboxPath))
            Directory.CreateDirectory(_inboxPath);

        // Recover crashed CreatePlan jobs: rename .processing files back to .md
        RecoverProcessingFiles();

        ProcessExistingFiles();

        _watcher = new FileSystemWatcher(_inboxPath, "*.md")
        {
            InternalBufferSize = 65536,
            NotifyFilter = NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Error += (_, e) =>
            CrashLog.Write($"[{DateTime.UtcNow:O}] InboxWatcher FSW error: {e.GetException()}");

        _pollTimer = new Timer(OnPollTimer, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _watcher?.Dispose();
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            _ = ProcessFileAsync(e.FullPath);
        }
        catch (Exception ex)
        {
            CrashLog.Write($"[{DateTime.UtcNow:O}] InboxWatcher.OnFileCreated exception: {ex}");
        }
    }

    private void OnPollTimer(object? state)
    {
        try
        {
            ProcessExistingFiles();
        }
        catch (Exception ex)
        {
            CrashLog.Write($"[{DateTime.UtcNow:O}] InboxWatcher.OnPollTimer exception: {ex}");
        }
    }

    internal void RecoverProcessingFiles()
    {
        if (!Directory.Exists(_inboxPath))
            return;

        foreach (var file in Directory.GetFiles(_inboxPath, "*.md.processing"))
            try
            {
                var mdPath = file[..^".processing".Length];
                if (File.Exists(mdPath))
                    // .md already exists — just delete the stale .processing file
                    File.Delete(file);
                else
                    File.Move(file, mdPath);
            }
            catch
            {
                _logger.LogWarning("Failed to recover inbox file {File}. It will be retried on next startup.", file);
            }
    }

    internal void ProcessExistingFiles()
    {
        if (!Directory.Exists(_inboxPath))
            return;

        foreach (var file in Directory.GetFiles(_inboxPath, "*.md"))
            _ = ProcessFileAsync(file);
    }

    private async Task ProcessFileAsync(string filePath)
    {
        if (!_processing.TryAdd(filePath, 0))
            return;

        try
        {
            // Wait briefly for the file to be fully written
            await Task.Delay(500);

            if (!File.Exists(filePath))
                return;

            // Skip if a CreatePlan job is already tracking this inbox file.
            // Guards against the FSW firing Created more than once for the same
            // file, the 30s poll overlapping with an in-flight StartJob, and any
            // future caller that re-writes an .md into Inbox while its
            // .md.processing sibling is mid-job.
            if (_jobService.IsInboxFileTracked(filePath + ".processing"))
                return;

            try
            {
                await ProcessInboxFileAsync(filePath);
            }
            catch (Exception ex)
            {
                // Retry once after a short delay
                await Task.Delay(1000);
                try
                {
                    await ProcessInboxFileAsync(filePath);
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx,
                        "Failed to process inbox file {FilePath} after retry. Initial error: {InitialError}",
                        filePath, ex.Message);
                }
            }
        }
        finally
        {
            _processing.TryRemove(filePath, out _);
        }
    }

    private async Task ProcessInboxFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        var content = await FileHelper.ReadAllTextAsync(filePath);
        var (project, description, sourcePath) = ParseContent(content);

        if (string.IsNullOrWhiteSpace(description))
        {
            _logger.LogWarning("Skipping inbox file {FilePath} — empty description.", filePath);
            return;
        }

        // Rename to .processing so the watcher/poller ignores it while the job runs
        var processingPath = filePath + ".processing";
        File.Move(filePath, processingPath);

        var args = new List<string> { "-Description", description, "-Project", project };
        if (!string.IsNullOrEmpty(sourcePath))
            args.AddRange(["-SourcePath", sourcePath]);
        _jobService.StartJob("CreatePlan", args.ToArray(), processingPath);
    }

    internal static (string project, string description, string? sourcePath) ParseContent(string content)
    {
        if (content.StartsWith("---"))
        {
            var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
            if (endIndex > 3)
            {
                var frontmatter = content.Substring(3, endIndex - 3).Trim();
                var description = content.Substring(endIndex + 3).Trim();

                string? project = null;
                string? sourcePath = null;

                foreach (var line in frontmatter.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
                        project = trimmed.Substring("project:".Length).Trim();
                    else if (trimmed.StartsWith("sourcePath:", StringComparison.OrdinalIgnoreCase))
                        sourcePath = trimmed.Substring("sourcePath:".Length).Trim();
                }

                return (project ?? "Auto", description, sourcePath);
            }
        }

        return ("Auto", content, null);
    }
}
