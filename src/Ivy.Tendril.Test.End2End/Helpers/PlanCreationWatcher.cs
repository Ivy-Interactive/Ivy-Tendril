namespace Ivy.Tendril.Test.End2End.Helpers;

public sealed class PlanCreationWatcher : IDisposable
{
    private readonly string _plansDir;
    private readonly string _titleFragment;
    private readonly TaskCompletionSource<string> _tcs = new();
    private FileSystemWatcher? _watcher;

    public PlanCreationWatcher(string plansDir, string titleFragment)
    {
        _plansDir = plansDir;
        _titleFragment = titleFragment;

        try
        {
            _watcher = new FileSystemWatcher(plansDir)
            {
                Filter = "*.*",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
        }
        catch
        {
            _watcher = null;
        }
    }

    public async Task<string> WaitAsync(TimeSpan timeout, IReadOnlyList<string>? stdoutLines = null)
    {
        using var cts = new CancellationTokenSource(timeout);

        if (TryCheckNow(out var folder))
            return folder!;

        // Race: FileSystemWatcher event vs polling fallback
        var pollTask = PollUntilFound(cts.Token, stdoutLines);
        var winnerTask = await Task.WhenAny(_tcs.Task, pollTask);
        return await winnerTask;
    }

    private async Task<string> PollUntilFound(CancellationToken ct, IReadOnlyList<string>? stdoutLines)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
            catch (OperationCanceledException) { break; }

            if (TryCheckNow(out var folder))
                return folder!;
        }

        throw BuildTimeoutException(stdoutLines);
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!e.FullPath.EndsWith("plan.yaml", StringComparison.OrdinalIgnoreCase)) return;
            var dir = Path.GetDirectoryName(e.FullPath)!;
            if (MatchesTitle(Path.GetFileName(dir)))
                _tcs.TrySetResult(dir);
        }
        catch { }
    }

    private bool TryCheckNow(out string? folder)
    {
        folder = null;
        if (!Directory.Exists(_plansDir)) return false;

        foreach (var dir in Directory.GetDirectories(_plansDir))
        {
            if (!MatchesTitle(Path.GetFileName(dir))) continue;
            if (File.Exists(Path.Combine(dir, "plan.yaml")))
            {
                folder = dir;
                return true;
            }
        }
        return false;
    }

    private bool MatchesTitle(string folderName)
    {
        var normalized = _titleFragment.Replace("-", "");
        return folderName.Contains(_titleFragment, StringComparison.OrdinalIgnoreCase) ||
               folderName.Replace("-", "").Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private TimeoutException BuildTimeoutException(IReadOnlyList<string>? stdoutLines)
    {
        var entries = Directory.Exists(_plansDir)
            ? string.Join(", ", Directory.GetFileSystemEntries(_plansDir).Select(Path.GetFileName))
            : "(dir missing)";
        var stdout = stdoutLines != null
            ? string.Join("\n", stdoutLines.TakeLast(30))
            : "";
        return new TimeoutException(
            $"Plan '{_titleFragment}' with plan.yaml not created.\n" +
            $"Plans dir: {entries}\n" +
            $"Tendril stdout (last 30):\n{stdout}");
    }

    public void Dispose()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
