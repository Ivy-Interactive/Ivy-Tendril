using Ivy.Tendril.Wireframe.Build;
using Spectre.Console;

namespace Ivy.Tendril.Commands.Wireframe;

/// <summary>
/// Shared provisioning for <c>--tailwind jit</c>: one place to download, report progress
/// and turn failures into an actionable message rather than a stack trace.
/// </summary>
public static class TailwindSupport
{
    /// <summary>
    /// Returns the Tailwind binary path, or null if it could not be provisioned (having
    /// already explained why).
    /// </summary>
    public static async Task<string?> ProvisionAsync(bool quiet, CancellationToken ct)
    {
        var provisioner = new TailwindProvisioner();

        try
        {
            if (quiet) return await provisioner.ResolveAsync(ct: ct);

            string? path = null;
            var lastReported = -1;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("provisioning the Tailwind CLI...", async status =>
                {
                    path = await provisioner.ResolveAsync((written, total) =>
                    {
                        // A silent 107 MB download is indistinguishable from a hang.
                        var mb = (int)(written / 1024 / 1024);
                        if (mb == lastReported) return;
                        lastReported = mb;

                        status.Status = total is { } t
                            ? $"downloading Tailwind {TailwindProvisioner.Version}  {mb} / {t / 1024 / 1024} MB"
                            : $"downloading Tailwind {TailwindProvisioner.Version}  {mb} MB";
                    }, ct);
                });

            if (lastReported >= 0)
                AnsiConsole.MarkupLine(
                    $"  [green]+[/] [grey]Tailwind {TailwindProvisioner.Version} cached at {TailwindProvisioner.CacheRoot.EscapeMarkup()}[/]");

            return path;
        }
        catch (Exception e) when (e is HttpRequestException or IOException
                                       or PlatformNotSupportedException or InvalidOperationException)
        {
            AnsiConsole.MarkupLine("[red]Could not provision the Tailwind standalone CLI.[/]");
            AnsiConsole.MarkupLine($"  {e.Message.EscapeMarkup()}");
            AnsiConsole.MarkupLine(
                "  [grey]Set WIREFRAME_TAILWIND to a local binary, or drop --tailwind jit to use the " +
                "embedded utility sheet.[/]");
            return null;
        }
    }
}
