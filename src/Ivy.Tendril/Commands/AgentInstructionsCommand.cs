using Ivy.Tendril.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public sealed class AgentInstructionsCommand(IConfigService config) : Command<AgentInstructionsCommand.Settings>
{
    public class Settings : CommandSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        var prompt = AgentPromptCompiler.Compile(config);
        if (prompt == null)
        {
            AnsiConsole.MarkupLine("[red]Failed to compile agent prompt (embedded resource missing)[/]");
            return 1;
        }

        Console.Write(prompt);
        return 0;
    }
}
