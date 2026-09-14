using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using System.Collections.Concurrent;
using System.Linq;
using Ivy.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class InboxWatcherService : IInboxWatcherService
{
    private readonly string _inboxPath;
    private readonly IJobService _jobService;
    private readonly ILogger<InboxWatcherService> _logger;
    private readonly ConcurrentDictionary<string, byte> _processing = new();
    private readonly object _startLock = new();
    private Timer? _pollTimer;
    private FileSystemWatcher? _watcher;
    private volatile bool _started;

    /// <summary>
    ///     Assigns dependencies and nothing else. Resolving this type from DI used to create the inbox
    ///     directory, rename every <c>.processing</c> file back to <c>.md</c> and arm a watcher plus a
    ///     poll timer, which meant a non-master instance resurrected the whole shared inbox merely by
    ///     being constructed. All of that now waits for <see cref="Start" />.
    /// </summary>
    public InboxWatcherService(IConfigService config, IJobService jobService, ILogger<InboxWatcherService> logger)
    {
        _jobService = jobService;
        _logger = logger;
        _inboxPath = Path.Combine(config.TendrilHome, "Inbox");
    }

    /// <summary>Idempotent: starting an already started watcher is a no-op.</summary>
    public void Start()
    {
        lock (_startLock)
        {
            if (_started)
                return;

            if (!Directory.Exists(_inboxPath))
                Directory.CreateDirectory(_inboxPath);

            // Recover crashed CreatePlan jobs: rename .processing files back to .md
            RecoverProcessingFiles();

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
            _started = true;
        }

        _ = Task.Run(ProcessExistingFilesAsync);
    }

    /// <summary>Idempotent: releases the watcher and the poll timer so this instance stops touching the shared inbox.</summary>
    public void Stop()
    {
        lock (_startLock)
        {
            if (!_started)
                return;

            _started = false;

            _pollTimer?.Dispose();
            _pollTimer = null;

            if (_watcher != null)
            {
                _watcher.Created -= OnFileCreated;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }

    public void Dispose() => Stop();

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        // An event queued before Stop() must not process a file afterwards.
        if (!_started) return;

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
        if (!_started) return;

        try
        {
            _ = ProcessExistingFilesAsync();
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

    internal async Task ProcessExistingFilesAsync()
    {
        // Start() hands this to the thread pool, so it can first run after a Stop() that a demotion
        // triggered in between. Every await below is another chance to be demoted, so the flag is
        // rechecked rather than read once.
        if (!_started) return;

        if (!Directory.Exists(_inboxPath))
            return;

        List<string> files;
        try
        {
            files = Directory.GetFiles(_inboxPath, "*.md")
                .OrderBy(f => File.GetCreationTimeUtc(f))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list inbox files for processing");
            return;
        }

        for (int i = 0; i < files.Count; i++)
        {
            if (!_started) return;

            _ = ProcessFileAsync(files[i]);

            // Stagger startup to avoid thundering herd
            if (i < files.Count - 1)
                await Task.Delay(2000);
        }
    }

    private async Task ProcessFileAsync(string filePath)
    {
        if (!_processing.TryAdd(filePath, 0))
            return;

        try
        {
            // Wait briefly for the file to be fully written
            await Task.Delay(500);

            // Renaming the file and starting a job is the side effect a demoted instance must not have,
            // and it is on the far side of that delay.
            if (!_started) return;

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
        try
        {
            File.Move(filePath, processingPath);
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning("Inbox file {FilePath} was deleted before processing — skipping.", filePath);
            return;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to rename inbox file {FilePath} to {ProcessingPath} — skipping.", filePath, processingPath);
            return;
        }

        var args = new CreatePlanArgs(description, project, SourcePath: sourcePath);
        _jobService.StartJob(args, processingPath);
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
