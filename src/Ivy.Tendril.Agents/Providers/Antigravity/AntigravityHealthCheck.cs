using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.Antigravity;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = BinaryResolver.FindOnPath("agy");
        if (path is null)
            return new AgentInstallStatus { IsInstalled = false, Error = "agy not found on PATH" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public async Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        // 1. Run process check with a short timeout to prevent UI hangs
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            "agy", ["models"], TimeSpan.FromSeconds(3), ct);

        if (exitCode == 0)
        {
            return new AgentAuthResult
            {
                Status = AuthStatus.Authenticated,
                AuthMethod = "oauth",
            };
        }

        // 2. Fallback: if process timed out or failed, check state files
        // (Keychain access can be blocked or prompt in non-interactive/test environments)
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var statePath = Path.Combine(home, ".gemini", "antigravity", "antigravity_state.pbtxt");
        if (File.Exists(statePath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(statePath, ct);
                if (content.Contains("AGENT_ONBOARDING_STATE_COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    return new AgentAuthResult
                    {
                        Status = AuthStatus.Authenticated,
                        AuthMethod = "oauth",
                    };
                }
            }
            catch
            {
                // Ignore file read issues
            }
        }

        return new AgentAuthResult
        {
            Status = AuthStatus.NotAuthenticated,
            Error = string.IsNullOrWhiteSpace(stderr) ? "Not authenticated" : stderr,
            SignInHint = "Run 'agy' and complete the browser-based auth flow",
        };
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "agy", ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    /// <summary>
    ///     Probes the model for real — including a non-<c>default</c> one, which used to be waved
    ///     through as <see cref="ModelValidationStatus.Unknown" /> with "Model validation not supported
    ///     via Antigravity CLI". That short-circuit is why <c>tendril doctor</c> reported a healthy
    ///     Antigravity while every job launched against it died on an exhausted quota.
    /// </summary>
    public async Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "--dangerously-skip-permissions",
            "--output-format", "stream-json",
            "--print-timeout", $"{(int)ProbeTimeout.TotalSeconds}s",
        };

        // "default" means "whatever the CLI picks", so passing it through as a model id would be a
        // request for a model that does not exist.
        var hasExplicitModel = !string.IsNullOrEmpty(model)
                               && !string.Equals(model, "default", StringComparison.OrdinalIgnoreCase);
        if (hasExplicitModel)
        {
            args.Add("--model");
            args.Add(model);
            args.Add("--effort");
            args.Add("medium");
        }

        args.Add("--print");
        args.Add(ProbePrompt);

        var (exitCode, stdout, stderr) = await HealthCheckRunner.RunAsync("agy", args, ProbeTimeout, ct);

        return MapProbeResult(model, exitCode, stdout, stderr);
    }

    /// <summary>
    ///     Timeout on the probe. A wedged CLI must not be able to hang <c>doctor</c>, and the prompt is
    ///     one word, so anything slower than this is a symptom in its own right.
    /// </summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     One word in, one word out: the cheapest round-trip that still proves the provider will serve
    ///     this model.
    /// </summary>
    internal const string ProbePrompt = "reply with the single word OK";

    /// <summary>
    ///     Maps a probe's process outcome onto a <see cref="ModelValidationResult" />. Separated from
    ///     the process launch so the mapping — where all the actual logic lives — is unit-testable
    ///     without a live <c>agy</c>.
    /// </summary>
    internal static ModelValidationResult MapProbeResult(string model, int exitCode, string stdout, string stderr)
    {
        // Reuse the real parser rather than hand-rolling JSON: it already knows that a terminal
        // `result` with status ERROR and no response is a failure, and carries the error text over.
        var parser = new AntigravityEventParser();
        ResultEvent? result = null;
        foreach (var line in (stdout ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var evt in parser.ParseLine(line))
            {
                if (evt is ResultEvent resultEvt)
                    result = resultEvt;
            }
        }

        if (result is { IsSuccess: true })
            return new ModelValidationResult { Status = ModelValidationStatus.Ok, Model = model };

        var detail = FirstNonBlank(result?.Error, stderr, stdout);

        // HealthCheckRunner reports a timeout as exit code -1 with "Timed out" on stderr.
        if (exitCode == -1 && string.Equals(stderr?.Trim(), "Timed out", StringComparison.OrdinalIgnoreCase))
            return new ModelValidationResult { Status = ModelValidationStatus.Timeout, Model = model };

        switch (ProviderErrorClassifier.Classify(detail))
        {
            case ProviderErrorClassifier.ProviderErrorKind.Quota:
                return new ModelValidationResult
                {
                    Status = ModelValidationStatus.RateLimit,
                    Model = model,
                    ErrorMessage = detail,
                };

            case ProviderErrorClassifier.ProviderErrorKind.Auth:
                return new ModelValidationResult
                {
                    Status = ModelValidationStatus.AuthError,
                    Model = model,
                    ErrorMessage = detail,
                };
        }

        if (detail != null && MentionsUnknownModel(detail))
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.InvalidModel,
                Model = model,
                ErrorMessage = detail,
            };

        // A clean exit with no parseable result line still means the CLI did the work (e.g. an older
        // build that emits nothing but text), so don't manufacture a failure from it.
        if (result == null && exitCode == 0)
            return new ModelValidationResult { Status = ModelValidationStatus.Ok, Model = model };

        return new ModelValidationResult
        {
            Status = ModelValidationStatus.Unknown,
            Model = model,
            ErrorMessage = detail,
        };
    }

    private static bool MentionsUnknownModel(string detail)
        => detail.Contains("unknown model", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("invalid model", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("model not found", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("unsupported model", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonBlank(params string?[] candidates)
        => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "Antigravity",
        InstallCommand = "agy install",
        InstallUrl = "https://antigravity.dev",
        AuthCommand = "agy",
        SignInHint = "Run 'agy' and complete the browser-based auth flow",
        DocsUrl = "https://antigravity.dev",
    };
}
