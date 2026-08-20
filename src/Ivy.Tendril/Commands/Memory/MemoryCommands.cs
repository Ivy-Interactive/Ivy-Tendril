using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using Ivy.Tendril.Services.Memory;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands.Memory;

public class BaseMemorySettings : CommandSettings
{
    [Description("Target project name (optional)")]
    [CommandOption("-p|--project <PROJECT>")]
    public string? Project { get; set; }
}

public class MemoryStatusCommand : Command<BaseMemorySettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryStatusCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, BaseMemorySettings settings, CancellationToken cancellationToken)
    {
        var status = _memoryService.GetStatus(projectName: settings.Project);
        AnsiConsole.WriteLine(status.RawOutput);
        return 0;
    }
}

public class MemoryAddSettings : BaseMemorySettings
{
    [Description("Memory note name (e.g. project-stack or auth-flow)")]
    [CommandArgument(0, "<NAME>")]
    public string Name { get; set; } = "";

    [Description("Memory note title")]
    [CommandOption("--title <TITLE>")]
    public string? Title { get; set; }

    [Description("Comma-separated tags")]
    [CommandOption("--tags <TAGS>")]
    public string? Tags { get; set; }
}

public class MemoryAddCommand : Command<MemoryAddSettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryAddCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, MemoryAddSettings settings, CancellationToken cancellationToken)
    {
        var tagsList = settings.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var note = _memoryService.AddMemory(settings.Name, settings.Title, tagsList, projectName: settings.Project);
        AnsiConsole.MarkupLine($"[green]Created memory note:[/] [bold]{note.Name}[/] at {note.Path}");
        return 0;
    }
}

public class MemoryReadSettings : BaseMemorySettings
{
    [Description("Memory note name")]
    [CommandArgument(0, "<NAME>")]
    public string Name { get; set; } = "";
}

public class MemoryReadCommand : Command<MemoryReadSettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryReadCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, MemoryReadSettings settings, CancellationToken cancellationToken)
    {
        var note = _memoryService.ReadMemory(settings.Name, projectName: settings.Project);
        if (note == null)
        {
            AnsiConsole.MarkupLine($"[red]Memory note not found:[/] {settings.Name}");
            return 1;
        }

        AnsiConsole.WriteLine(note.Content);
        return 0;
    }
}

public class MemoryWriteSettings : BaseMemorySettings
{
    [Description("Memory note name")]
    [CommandArgument(0, "<NAME>")]
    public string Name { get; set; } = "";

    [Description("Content string to write (reads stdin if omitted)")]
    [CommandOption("-c|--content <CONTENT>")]
    public string? Content { get; set; }
}

public class MemoryWriteCommand : Command<MemoryWriteSettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryWriteCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, MemoryWriteSettings settings, CancellationToken cancellationToken)
    {
        var contentToWrite = settings.Content;
        if (string.IsNullOrEmpty(contentToWrite) && Console.IsInputRedirected)
        {
            contentToWrite = Console.In.ReadToEnd();
        }

        if (string.IsNullOrEmpty(contentToWrite))
        {
            AnsiConsole.MarkupLine("[red]Error: Content must be provided via --content option or stdin redirect.[/]");
            return 1;
        }

        _memoryService.WriteMemory(settings.Name, contentToWrite, projectName: settings.Project);
        AnsiConsole.MarkupLine($"[green]Updated memory note:[/] [bold]{settings.Name}[/]");
        return 0;
    }
}

public class MemoryLinkSettings : BaseMemorySettings
{
    [Description("Memory note name")]
    [CommandArgument(0, "<NAME>")]
    public string Name { get; set; } = "";

    [Description("Relative code file path to link")]
    [CommandArgument(1, "<FILE>")]
    public string FilePath { get; set; } = "";
}

public class MemoryLinkCommand : Command<MemoryLinkSettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryLinkCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, MemoryLinkSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            _memoryService.LinkFile(settings.Name, settings.FilePath, projectName: settings.Project);
            AnsiConsole.MarkupLine($"[green]Linked file[/] {settings.FilePath} [green]to note:[/] [bold]{settings.Name}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error linking file:[/] {ex.Message}");
            return 1;
        }
    }
}

public class MemoryUpdateSettings : BaseMemorySettings
{
    [Description("Memory note name")]
    [CommandArgument(0, "<NAME>")]
    public string Name { get; set; } = "";
}

public class MemoryUpdateCommand : Command<MemoryUpdateSettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryUpdateCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, MemoryUpdateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            _memoryService.UpdateMemory(settings.Name, projectName: settings.Project);
            AnsiConsole.MarkupLine($"[green]Synchronized reference hashes for note:[/] [bold]{settings.Name}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error updating note:[/] {ex.Message}");
            return 1;
        }
    }
}

public class MemoryRelateSettings : BaseMemorySettings
{
    [Description("Source memory note name")]
    [CommandArgument(0, "<SOURCE>")]
    public string Source { get; set; } = "";

    [Description("Target memory note name")]
    [CommandArgument(1, "<TARGET>")]
    public string Target { get; set; } = "";
}

public class MemoryRelateCommand : Command<MemoryRelateSettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryRelateCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, MemoryRelateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            _memoryService.RelateMemories(settings.Source, settings.Target, projectName: settings.Project);
            AnsiConsole.MarkupLine($"[green]Related note[/] [bold]{settings.Source}[/] -> [bold]{settings.Target}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error relating notes:[/] {ex.Message}");
            return 1;
        }
    }
}

public class MemoryDeleteSettings : BaseMemorySettings
{
    [Description("Memory note name to delete")]
    [CommandArgument(0, "<NAME>")]
    public string Name { get; set; } = "";
}

public class MemoryDeleteCommand : Command<MemoryDeleteSettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryDeleteCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, MemoryDeleteSettings settings, CancellationToken cancellationToken)
    {
        _memoryService.DeleteMemory(settings.Name, projectName: settings.Project);
        AnsiConsole.MarkupLine($"[green]Deleted memory note:[/] [bold]{settings.Name}[/]");
        return 0;
    }
}

public class MemoryQuerySettings : BaseMemorySettings
{
    [Description("Search keyword or pattern")]
    [CommandArgument(0, "<KEYWORD>")]
    public string Keyword { get; set; } = "";
}

public class MemoryQueryCommand : Command<MemoryQuerySettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryQueryCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, MemoryQuerySettings settings, CancellationToken cancellationToken)
    {
        var notes = _memoryService.QueryMemories(settings.Keyword, projectName: settings.Project);
        foreach (var note in notes)
        {
            AnsiConsole.MarkupLine($"[bold]{note.Name}[/] - [dim]{note.Title}[/]");
        }
        return 0;
    }
}

public class MemoryRulesCommand : Command<BaseMemorySettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryRulesCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, BaseMemorySettings settings, CancellationToken cancellationToken)
    {
        var rules = _memoryService.GetRulesMarkdown(projectName: settings.Project);
        AnsiConsole.WriteLine(rules);
        return 0;
    }
}

public class MemoryPurgeCommand : Command<BaseMemorySettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryPurgeCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, BaseMemorySettings settings, CancellationToken cancellationToken)
    {
        int count = _memoryService.PurgeMemories(projectName: settings.Project);
        AnsiConsole.MarkupLine($"[green]Purged [bold]{count}[/] memory note(s).[/]");
        return 0;
    }
}

public class MemoryCompactCommand : Command<BaseMemorySettings>
{
    private readonly IMemoryService _memoryService;

    public MemoryCompactCommand(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    protected override int Execute(CommandContext context, BaseMemorySettings settings, CancellationToken cancellationToken)
    {
        int count = _memoryService.CompactMemories(projectName: settings.Project);
        AnsiConsole.MarkupLine($"[green]Compacted [bold]{count}[/] memory note(s).[/]");
        return 0;
    }
}
