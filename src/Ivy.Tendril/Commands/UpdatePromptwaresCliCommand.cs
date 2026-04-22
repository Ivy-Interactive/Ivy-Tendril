using Spectre.Console.Cli;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Commands;

public class UpdatePromptwaresSettings : CommandSettings
{
}

public class UpdatePromptwaresCliCommand : Command<UpdatePromptwaresSettings>
{
    protected override int Execute(CommandContext context, UpdatePromptwaresSettings settings, CancellationToken cancellationToken)
    {
        return PromptwareCommands.UpdatePromptwaresCommandInternal();
    }
}
