using System.ComponentModel;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class DoctorSettings : CommandSettings
{
}

public class DoctorPlansSettings : CommandSettings
{
    [CommandOption("--all")]
    [Description("Show all plans including completed ones")]
    public bool All { get; init; }

    [CommandOption("--fix")]
    [Description("Fix detected issues")]
    public bool Fix { get; init; }

    [CommandOption("--prune")]
    [Description("Prune invalid plans")]
    public bool Prune { get; init; }

    [CommandOption("--state")]
    [Description("Filter plans by state")]
    public string? State { get; init; }

    [CommandOption("--worktrees")]
    [Description("Check worktrees only")]
    public bool WorktreesOnly { get; init; }
}

public class DoctorCliCommand : AsyncCommand<DoctorSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings, CancellationToken cancellationToken)
    {
        return await DoctorCommand.RunAsync();
    }
}

public class DoctorPlansCommand : Command<DoctorPlansSettings>
{
    protected override int Execute(CommandContext context, DoctorPlansSettings settings, CancellationToken cancellationToken)
    {
        var args = new List<string>();
        if (settings.All) args.Add("--all");
        if (settings.Fix) args.Add("--fix");
        if (settings.Prune) args.Add("--prune");
        if (!string.IsNullOrEmpty(settings.State))
        {
            args.Add("--state");
            args.Add(settings.State);
        }
        if (settings.WorktreesOnly) args.Add("--worktrees");

        return DoctorCommand.DoctorPlansInternal(args.ToArray());
    }
}
