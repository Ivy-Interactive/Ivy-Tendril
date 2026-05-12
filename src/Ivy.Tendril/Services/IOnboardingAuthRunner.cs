namespace Ivy.Tendril.Services;

public interface IOnboardingAuthRunner
{
    /// <summary>
    /// Spawns the CLI auth flow for the given tool key, opens any detected URL via the
    /// supplied <see cref="IClientProvider"/>, and waits for the process to exit.
    /// </summary>
    /// <param name="toolKey">Tool key (gh, claude, codex, gemini, copilot).</param>
    /// <param name="client">Client used to open the auth URL in the user's browser.</param>
    /// <param name="onCode">Callback invoked when a one-time device code is detected in stdout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the process exited with code 0.</returns>
    Task<bool> RunAuthAsync(string toolKey, IClientProvider client, Action<string> onCode, CancellationToken ct);
}
