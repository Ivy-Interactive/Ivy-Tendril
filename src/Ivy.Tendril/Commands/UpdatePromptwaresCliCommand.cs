using Spectre.Console.Cli;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Commands;

public class UpdatePromptwaresSettings : CommandSettings
{
}

public class UpdatePromptwaresCliCommand : Command<UpdatePromptwaresSettings>
{
    public override int Execute(CommandContext context, UpdatePromptwaresSettings settings)
    {
        return PromptwareCommands.UpdatePromptwaresCommandInternal();
    }
}
