using System.ComponentModel;
using Ivy.Tendril.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

// --- Settings ---

public class ProjectListSettings : CommandSettings { }

public class ProjectAddSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [CommandOption("--color")]
    [Description("Project color")]
    public string? Color { get; set; }

    [CommandOption("--context")]
    [Description("Project context/prompt")]
    public string? Context { get; set; }
}

public class ProjectGetSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";
}

public class ProjectRemoveSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";
}

public class ProjectSetSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Field name (name, color, context)")]
    [CommandArgument(1, "<field>")]
    public string Field { get; set; } = "";

    [Description("Field value")]
    [CommandArgument(2, "<value>")]
    public string Value { get; set; } = "";
}

public class ProjectAddRepoSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Repository path")]
    [CommandArgument(1, "<repo-path>")]
    public string RepoPath { get; set; } = "";

    [CommandOption("--pr-rule")]
    [Description("PR rule (default, yolo)")]
    public string? PrRule { get; set; }

    [CommandOption("--base-branch")]
    [Description("Base branch name")]
    public string? BaseBranch { get; set; }
}

public class ProjectRemoveRepoSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Repository path")]
    [CommandArgument(1, "<repo-path>")]
    public string RepoPath { get; set; } = "";
}

public class ProjectAddVerificationSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Verification name")]
    [CommandArgument(1, "<verification-name>")]
    public string VerificationName { get; set; } = "";

    [CommandOption("--required")]
    [Description("Whether the verification is required")]
    public bool Required { get; set; }

    [CommandOption("--after")]
    [Description("Place after this verification (default: append to end)")]
    public string? After { get; set; }
}

public class ProjectRemoveVerificationSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Verification name")]
    [CommandArgument(1, "<verification-name>")]
    public string VerificationName { get; set; } = "";
}

public class ProjectMoveVerificationSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Verification name to move")]
    [CommandArgument(1, "<verification-name>")]
    public string VerificationName { get; set; } = "";

    [CommandOption("--before")]
    [Description("Place before this verification")]
    public string? Before { get; set; }

    [CommandOption("--after")]
    [Description("Place after this verification")]
    public string? After { get; set; }

    [CommandOption("--position")]
    [Description("Place at this zero-based index position")]
    public int? Position { get; set; }
}

public class ProjectAddBuildDepSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Build dependency")]
    [CommandArgument(1, "<dependency>")]
    public string Dependency { get; set; } = "";
}

public class ProjectRemoveBuildDepSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Build dependency")]
    [CommandArgument(1, "<dependency>")]
    public string Dependency { get; set; } = "";
}

public class ProjectAddReviewActionSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Review action name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";

    [CommandOption("--command")]
    [Description("Command to execute")]
    public string? Command { get; set; }

    [CommandOption("--condition")]
    [Description("Condition expression (e.g. Test-Path \"...\")")]
    public string? Condition { get; set; }
}

public class ProjectRemoveReviewActionSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Review action name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";
}

// --- Commands ---

public class ProjectListCommand : Command<ProjectListSettings>
{
    protected override int Execute(CommandContext context, ProjectListSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var projects = config.Settings.Projects;

        if (projects.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No projects found.[/]");
            return 0;
        }

        foreach (var p in projects)
            AnsiConsole.MarkupLine(p.Name.EscapeMarkup());
        return 0;
    }
}

public class ProjectGetCommand : Command<ProjectGetSettings>
{
    protected override int Execute(CommandContext context, ProjectGetSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.Name}");

        AnsiConsole.MarkupLine($"[bold]{project.Name.EscapeMarkup()}[/]");
        if (!string.IsNullOrEmpty(project.Color))
            AnsiConsole.MarkupLine($"  Color: {project.Color.EscapeMarkup()}");
        if (!string.IsNullOrEmpty(project.Context))
            AnsiConsole.MarkupLine($"  Context: {project.Context.EscapeMarkup()}");

        if (project.Repos.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Repositories[/]");
            var repoTable = new Spectre.Console.Table();
            repoTable.AddColumn("Path");
            repoTable.AddColumn("PR Rule");
            repoTable.AddColumn("Base Branch");
            foreach (var r in project.Repos)
                repoTable.AddRow(
                    r.Path.EscapeMarkup(),
                    r.PrRule.EscapeMarkup(),
                    (r.BaseBranch ?? "-").EscapeMarkup());
            AnsiConsole.Write(repoTable);
        }

        if (project.Verifications.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Verifications[/]");
            var verTable = new Spectre.Console.Table();
            verTable.AddColumn("Name");
            verTable.AddColumn("Required");
            foreach (var v in project.Verifications)
                verTable.AddRow(v.Name.EscapeMarkup(), v.Required ? "Yes" : "No");
            AnsiConsole.Write(verTable);
        }

        if (project.ReviewActions.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Review Actions[/]");
            var raTable = new Spectre.Console.Table();
            raTable.AddColumn("Name");
            raTable.AddColumn("Command");
            raTable.AddColumn("Condition");
            foreach (var ra in project.ReviewActions)
                raTable.AddRow(
                    ra.Name.EscapeMarkup(),
                    ra.Command.EscapeMarkup(),
                    ra.Condition.EscapeMarkup());
            AnsiConsole.Write(raTable);
        }

        if (project.BuildDependencies.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Build Dependencies[/]");
            foreach (var dep in project.BuildDependencies)
                AnsiConsole.MarkupLine($"  - {dep.EscapeMarkup()}");
        }

        return 0;
    }
}

public class ProjectAddCommand : Command<ProjectAddSettings>
{
    protected override int Execute(CommandContext context, ProjectAddSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        if (config.Settings.Projects.Any(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Project already exists: {settings.Name}");

        config.Settings.Projects.Add(new ProjectConfig
        {
            Name = settings.Name,
            Color = settings.Color ?? "",
            Context = settings.Context ?? ""
        });

        config.SaveSettings();
        Console.WriteLine($"Added project: {settings.Name}");
        return 0;
    }
}

public class ProjectRemoveCommand : Command<ProjectRemoveSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var match = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new InvalidOperationException($"Project not found: {settings.Name}");

        config.Settings.Projects.Remove(match);
        config.SaveSettings();
        Console.WriteLine($"Removed project: {settings.Name}");
        return 0;
    }
}

public class ProjectSetCommand : Command<ProjectSetSettings>
{
    protected override int Execute(CommandContext context, ProjectSetSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var match = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new InvalidOperationException($"Project not found: {settings.Name}");

        switch (settings.Field.ToLower())
        {
            case "name":
                match.Name = settings.Value;
                break;
            case "color":
                match.Color = settings.Value;
                break;
            case "context":
                match.Context = settings.Value;
                break;
            default:
                throw new ArgumentException($"Unknown field: {settings.Field}. Valid fields: name, color, context");
        }

        config.SaveSettings();
        Console.WriteLine($"Updated project {settings.Field} to '{settings.Value}'");
        return 0;
    }
}

public class ProjectAddRepoCommand : Command<ProjectAddRepoSettings>
{
    protected override int Execute(CommandContext context, ProjectAddRepoSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.ProjectName}");

        if (project.GetRepoRef(settings.RepoPath) != null)
            throw new InvalidOperationException($"Repository already exists in project: {settings.RepoPath}");

        if (!string.IsNullOrWhiteSpace(settings.BaseBranch))
        {
            var isValid = Ivy.Tendril.Helpers.GitHelper.IsValidBranchAsync(settings.RepoPath, settings.BaseBranch, config.TendrilHome).GetAwaiter().GetResult();
            if (!isValid)
                throw new InvalidOperationException($"Branch '{settings.BaseBranch}' does not exist in repository: {settings.RepoPath}");
        }

        project.Repos.Add(new RepoRef
        {
            Path = settings.RepoPath,
            PrRule = settings.PrRule ?? "default",
            BaseBranch = settings.BaseBranch
        });

        config.SaveSettings();
        Console.WriteLine($"Added repository: {settings.RepoPath}");
        return 0;
    }
}

public class ProjectRemoveRepoCommand : Command<ProjectRemoveRepoSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveRepoSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.ProjectName}");

        var match = project.GetRepoRef(settings.RepoPath);
        if (match == null)
            throw new InvalidOperationException($"Repository not found in project: {settings.RepoPath}");

        project.Repos.Remove(match);
        config.SaveSettings();
        Console.WriteLine($"Removed repository: {settings.RepoPath}");
        return 0;
    }
}

public class ProjectAddVerificationCommand : Command<ProjectAddVerificationSettings>
{
    protected override int Execute(CommandContext context, ProjectAddVerificationSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.ProjectName}");

        if (project.Verifications.Any(v => v.Name.Equals(settings.VerificationName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Verification already exists in project: {settings.VerificationName}");

        var newRef = new ProjectVerificationRef
        {
            Name = settings.VerificationName,
            Required = settings.Required
        };

        if (!string.IsNullOrEmpty(settings.After))
        {
            var afterIndex = project.Verifications
                .FindIndex(v => v.Name.Equals(settings.After, StringComparison.OrdinalIgnoreCase));
            if (afterIndex < 0)
                throw new InvalidOperationException($"Verification not found for --after: {settings.After}");
            project.Verifications.Insert(afterIndex + 1, newRef);
        }
        else
        {
            project.Verifications.Add(newRef);
        }

        config.SaveSettings();
        Console.WriteLine($"Added verification: {settings.VerificationName}");
        return 0;
    }
}

public class ProjectRemoveVerificationCommand : Command<ProjectRemoveVerificationSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveVerificationSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.ProjectName}");

        var match = project.Verifications
            .FirstOrDefault(v => v.Name.Equals(settings.VerificationName, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new InvalidOperationException($"Verification not found in project: {settings.VerificationName}");

        project.Verifications.Remove(match);
        config.SaveSettings();
        Console.WriteLine($"Removed verification: {settings.VerificationName}");
        return 0;
    }
}

public class ProjectMoveVerificationCommand : Command<ProjectMoveVerificationSettings>
{
    protected override int Execute(CommandContext context, ProjectMoveVerificationSettings settings, CancellationToken cancellationToken)
    {
        var optionCount = (settings.Before != null ? 1 : 0) + (settings.After != null ? 1 : 0) + (settings.Position != null ? 1 : 0);
        if (optionCount != 1)
            throw new ArgumentException("Specify exactly one of --before, --after, or --position");

        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.ProjectName}");

        var item = project.Verifications
            .FirstOrDefault(v => v.Name.Equals(settings.VerificationName, StringComparison.OrdinalIgnoreCase));

        if (item == null)
            throw new InvalidOperationException($"Verification not found in project: {settings.VerificationName}");

        project.Verifications.Remove(item);

        int insertIndex;
        if (settings.Before != null)
        {
            var targetIndex = project.Verifications
                .FindIndex(v => v.Name.Equals(settings.Before, StringComparison.OrdinalIgnoreCase));
            if (targetIndex < 0)
            {
                project.Verifications.Add(item);
                throw new InvalidOperationException($"Target verification not found for --before: {settings.Before}");
            }
            insertIndex = targetIndex;
        }
        else if (settings.After != null)
        {
            var targetIndex = project.Verifications
                .FindIndex(v => v.Name.Equals(settings.After, StringComparison.OrdinalIgnoreCase));
            if (targetIndex < 0)
            {
                project.Verifications.Add(item);
                throw new InvalidOperationException($"Target verification not found for --after: {settings.After}");
            }
            insertIndex = targetIndex + 1;
        }
        else
        {
            insertIndex = Math.Clamp(settings.Position!.Value, 0, project.Verifications.Count);
        }

        project.Verifications.Insert(insertIndex, item);
        config.SaveSettings();
        Console.WriteLine($"Moved verification '{settings.VerificationName}' to position {insertIndex}");
        return 0;
    }
}

public class ProjectAddBuildDepCommand : Command<ProjectAddBuildDepSettings>
{
    protected override int Execute(CommandContext context, ProjectAddBuildDepSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.ProjectName}");

        if (project.BuildDependencies.Contains(settings.Dependency, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Build dependency already exists: {settings.Dependency}");

        project.BuildDependencies.Add(settings.Dependency);
        config.SaveSettings();
        Console.WriteLine($"Added build dependency: {settings.Dependency}");
        return 0;
    }
}

public class ProjectRemoveBuildDepCommand : Command<ProjectRemoveBuildDepSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveBuildDepSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.ProjectName}");

        var removed = project.BuildDependencies.RemoveAll(d => d.Equals(settings.Dependency, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            throw new InvalidOperationException($"Build dependency not found: {settings.Dependency}");

        config.SaveSettings();
        Console.WriteLine($"Removed build dependency: {settings.Dependency}");
        return 0;
    }
}

public class ProjectAddReviewActionCommand : Command<ProjectAddReviewActionSettings>
{
    protected override int Execute(CommandContext context, ProjectAddReviewActionSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.ProjectName}");

        if (project.ReviewActions.Any(r => r.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Review action already exists: {settings.Name}");

        project.ReviewActions.Add(new ReviewActionConfig
        {
            Name = settings.Name,
            Command = settings.Command ?? "",
            Condition = settings.Condition ?? ""
        });

        config.SaveSettings();
        Console.WriteLine($"Added review action: {settings.Name}");
        return 0;
    }
}

public class ProjectRemoveReviewActionCommand : Command<ProjectRemoveReviewActionSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveReviewActionSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            throw new InvalidOperationException($"Project not found: {settings.ProjectName}");

        var match = project.ReviewActions
            .FirstOrDefault(r => r.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new InvalidOperationException($"Review action not found: {settings.Name}");

        project.ReviewActions.Remove(match);
        config.SaveSettings();
        Console.WriteLine($"Removed review action: {settings.Name}");
        return 0;
    }
}
