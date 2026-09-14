using System.ComponentModel;
using System.Text.Json;
using Ivy.Tendril.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class MasterStatusSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit the report as JSON instead of a table")]
    public bool Json { get; init; }
}

/// <summary>
///     Reports what the master claim says and whether the process it names is actually serving, without
///     touching the file. The recovery path for "every tendril command says the server is hung".
/// </summary>
public class MasterStatusCommand : Command<MasterStatusSettings>
{
    protected override int Execute(CommandContext context, MasterStatusSettings settings, CancellationToken cancellationToken)
    {
        var report = MasterStatus.Describe(PathHelper.GetDefaultTendrilHome());

        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, MasterStatus.JsonOptions));
            return MasterStatus.ExitCode(report);
        }

        Render(report);
        return MasterStatus.ExitCode(report);
    }

    private static void Render(MasterStatusReport report)
    {
        if (!report.FileExists)
        {
            AnsiConsole.MarkupLine($"[bold]Master claim:[/] {report.Path.EscapeMarkup()} [dim](missing)[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(report.Explanation.EscapeMarkup());
            return;
        }

        AnsiConsole.MarkupLine($"[bold]Master claim:[/] {report.Path.EscapeMarkup()}");

        if (report.ParseError != null)
        {
            AnsiConsole.MarkupLine($"  [red]Unreadable[/]  {report.ParseError.EscapeMarkup()}");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(report.Explanation.EscapeMarkup());
            return;
        }

        var liveness = report.ProcessAlive ? "[green]alive[/]" : "[red]not running[/]";
        AnsiConsole.MarkupLine($"  PID         {report.Pid} ({liveness})");
        AnsiConsole.MarkupLine($"  Port        {(report.Port == 0 ? "[yellow]not published yet[/]" : report.Port.ToString())}");
        AnsiConsole.MarkupLine($"  Scheme      {report.Scheme.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  Started     {report.StartedAt:O} [dim]({Ago(DateTime.UtcNow - report.StartedAt)} ago)[/]");

        var heartbeat = report.HeartbeatWithinThreshold
            ? $"[dim]({Ago(report.HeartbeatAge)} ago)[/]"
            : $"[yellow]({Ago(report.HeartbeatAge)} ago, STALE: threshold {report.Threshold.TotalSeconds:0}s)[/]";
        AnsiConsole.MarkupLine($"  Heartbeat   {report.Heartbeat:O} {heartbeat}");

        AnsiConsole.MarkupLine(report.HealthAnswered
            ? $"  Health      {report.SchemeThatAnswered}://localhost:{report.Port}/ivy/health [green]answered[/]"
            : $"  Health      [red]no answer[/] on {report.Scheme}://localhost:{report.Port}/ivy/health");

        AnsiConsole.WriteLine();
        var colour = MasterStatus.ExitCode(report) == 0 ? "green" : "yellow";
        AnsiConsole.MarkupLine($"[{colour}]{report.Explanation.EscapeMarkup()}[/]");
    }

    private static string Ago(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h{span.Minutes:00}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m{span.Seconds:00}s";
        return $"{span.TotalSeconds:0}s";
    }
}

public class MasterReleaseSettings : CommandSettings
{
    [CommandOption("--yes|-y")]
    [Description("Skip the confirmation prompt")]
    public bool Yes { get; init; }

    [CommandOption("--force")]
    [Description("Release even a master that is alive and answering")]
    public bool Force { get; init; }
}

/// <summary>
///     Breaks a wedged master claim deliberately, which is what the incident recovery needed and what
///     hand-writing the file was standing in for.
/// </summary>
public class MasterReleaseCommand : Command<MasterReleaseSettings>
{
    protected override int Execute(CommandContext context, MasterReleaseSettings settings, CancellationToken cancellationToken)
    {
        var home = PathHelper.GetDefaultTendrilHome();
        var report = MasterStatus.Describe(home);
        var decision = MasterStatus.DecideRelease(report, settings.Force);

        switch (decision.Verdict)
        {
            case MasterReleaseVerdict.NothingToRelease:
                AnsiConsole.MarkupLine(decision.Message.EscapeMarkup());
                return 0;

            case MasterReleaseVerdict.RefuseServing:
                AnsiConsole.MarkupLine($"[red]{CliOutput.Glyph(false)}[/] {decision.Message.EscapeMarkup()}");
                return 1;
        }

        if (!settings.Yes && !settings.Force)
        {
            if (!AnsiConsole.Profile.Capabilities.Interactive)
            {
                AnsiConsole.MarkupLine(
                    "[red]Cannot prompt for confirmation here.[/] Pass --yes to release the claim, or --force to " +
                    "release one whose holder is still answering.");
                return 1;
            }

            if (!AnsiConsole.Confirm(decision.Message.EscapeMarkup(), false))
            {
                AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
                return 0;
            }
        }

        AnsiConsole.MarkupLine($"[dim]{report.Explanation.EscapeMarkup()}[/]");

        if (!MasterLock.ForceRelease(home))
        {
            AnsiConsole.MarkupLine($"[red]{CliOutput.Glyph(false)}[/] Failed to delete {report.Path.EscapeMarkup()}.");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]{CliOutput.Glyph(true)}[/] Released the master claim at {report.Path.EscapeMarkup()}.");
        AnsiConsole.MarkupLine("[dim]Start a server with 'tendril', or let a running one re-assert its claim on the next beat.[/]");
        return 0;
    }
}
