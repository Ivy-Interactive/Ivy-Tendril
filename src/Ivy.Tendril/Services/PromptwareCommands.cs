using Spectre.Console;

namespace Ivy.Tendril.Services;

public static class PromptwareCommands
{
    /// <summary>
    ///     Handles promptware CLI commands. Returns exit code (0 = success, 1 = error),
    ///     or -1 if the args don't match a promptware command.
    /// </summary>
    public static int Handle(string[] args)
    {
        if (args.Length == 0) return -1;

        return args[0] switch
        {
            "update-promptwares" => UpdatePromptwaresCommandInternal(),
            _ => -1
        };
    }

    public static int UpdatePromptwaresCommandInternal()
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrEmpty(tendrilHome))
        {
            AnsiConsole.MarkupLine("[red]Error: TENDRIL_HOME environment variable is not set.[/]");
            return 1;
        }

        if (!PromptwareDeployer.IsEmbeddedAvailable())
        {
            AnsiConsole.MarkupLine("[red]Error: No embedded promptwares found in this build.[/]");
            return 1;
        }

        var target = Path.Combine(tendrilHome, "Promptwares");
        AnsiConsole.MarkupLine($"[bold]Updating promptwares in[/] [blue]{target}[/]...");
        PromptwareDeployer.Deploy(target);
        AnsiConsole.MarkupLine("[green]✓[/] Done.");
        return 0;
    }
}
