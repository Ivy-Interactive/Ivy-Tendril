namespace Ivy.Tendril.Test.End2End.Helpers;

public sealed class PlanStateWatcher : IDisposable
{
    private readonly string _plansDir;
    private readonly TaskCompletionSource<string> _tcs = new();
    private FileSystemWatcher? _watcher;
    private readonly string _titleFragment;
    private readonly string _expectedState;
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
        { "Failed", "Timeout", "Skipped" };

    public PlanStateWatcher(string plansDir, string titleFragment, string expectedState)
    {
        _plansDir = plansDir;
        _titleFragment = titleFragment;
        _expectedState = expectedState;

        try
        {
            _watcher = new FileSystemWatcher(plansDir)
            {
                Filter = "plan.yaml",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
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

        var pollTask = PollUntilFound(cts.Token, stdoutLines);
        var winnerTask = await Task.WhenAny(_tcs.Task, pollTask);
        return await winnerTask;
    }

    private async Task<string> PollUntilFound(CancellationToken ct, IReadOnlyList<string>? stdoutLines)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
            catch (OperationCanceledException) { break; }

            if (TryCheckNow(out var folder))
                return folder!;
        }

        throw BuildTimeoutException(stdoutLines);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(e.FullPath)!;
            var folderName = Path.GetFileName(dir);
            if (!MatchesTitle(folderName)) return;

            var content = ReadFileWithRetry(e.FullPath);
            if (content == null) return;
            CheckContent(content, dir);
        }
        catch { }
    }

    private void CheckContent(string content, string dir)
    {
        if (content.Contains($"state: {_expectedState}", StringComparison.OrdinalIgnoreCase))
        {
            _tcs.TrySetResult(dir);
            return;
        }

        foreach (var terminal in TerminalStates)
        {
            if (terminal.Equals(_expectedState, StringComparison.OrdinalIgnoreCase)) continue;
            if (content.Contains($"state: {terminal}", StringComparison.OrdinalIgnoreCase))
            {
                _tcs.TrySetException(new InvalidOperationException(
                    $"Plan reached terminal state '{terminal}' instead of expected '{_expectedState}'."));
                return;
            }
        }
    }

    private bool TryCheckNow(out string? folder)
    {
        folder = null;
        if (!Directory.Exists(_plansDir)) return false;

        foreach (var dir in Directory.GetDirectories(_plansDir))
        {
            if (!MatchesTitle(Path.GetFileName(dir))) continue;
            var yamlPath = Path.Combine(dir, "plan.yaml");
            if (!File.Exists(yamlPath)) continue;

            var content = ReadFileWithRetry(yamlPath);
            if (content == null) continue;

            if (content.Contains($"state: {_expectedState}", StringComparison.OrdinalIgnoreCase))
            {
                folder = dir;
                return true;
            }

            foreach (var terminal in TerminalStates)
            {
                if (terminal.Equals(_expectedState, StringComparison.OrdinalIgnoreCase)) continue;
                if (content.Contains($"state: {terminal}", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Plan reached terminal state '{terminal}' instead of expected '{_expectedState}'.");
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
            $"Plan '{_titleFragment}' did not reach state '{_expectedState}'.\n" +
            $"Plans dir: {entries}\n" +
            $"Tendril stdout (last 30):\n{stdout}");
    }

    private static string? ReadFileWithRetry(string path, int attempts = 3)
    {
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(50);
            }
        }
        return null;
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
