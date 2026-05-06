using System.ComponentModel;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
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

    [CommandOption("--sync-strategy")]
    [Description("Sync strategy (fetch, pull)")]
    public string? SyncStrategy { get; set; }
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
    private readonly ILogger<ProjectListCommand> _logger;

    public ProjectListCommand(ILogger<ProjectListCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectListSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var projects = config.Settings.Projects;

            if (projects.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]No projects found.[/]");
                return 0;
            }

            var table = new Spectre.Console.Table();
            table.AddColumn("Name");
            table.AddColumn("Color");
            table.AddColumn("Repos");
            table.AddColumn("Verifications");

            foreach (var p in projects)
                table.AddRow(
                    p.Name.EscapeMarkup(),
                    (p.Color ?? "-").EscapeMarkup(),
                    p.Repos.Count.ToString(),
                    p.Verifications.Count.ToString());

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list projects");
            return 1;
        }
    }
}

public class ProjectAddCommand : Command<ProjectAddSettings>
{
    private readonly ILogger<ProjectAddCommand> _logger;

    public ProjectAddCommand(ILogger<ProjectAddCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectAddSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();

            if (config.Settings.Projects.Any(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogError("Project already exists: {Name}", settings.Name);
                return 1;
            }

            config.Settings.Projects.Add(new ProjectConfig
            {
                Name = settings.Name,
                Color = settings.Color ?? "",
                Context = settings.Context ?? ""
            });

            config.SaveSettings();
            _logger.LogInformation("Added project: {Name}", settings.Name);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add project");
            return 1;
        }
    }
}

public class ProjectRemoveCommand : Command<ProjectRemoveSettings>
{
    private readonly ILogger<ProjectRemoveCommand> _logger;

    public ProjectRemoveCommand(ILogger<ProjectRemoveCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectRemoveSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var match = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                _logger.LogError("Project not found: {Name}", settings.Name);
                return 1;
            }

            config.Settings.Projects.Remove(match);
            config.SaveSettings();
            _logger.LogInformation("Removed project: {Name}", settings.Name);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove project");
            return 1;
        }
    }
}

public class ProjectSetCommand : Command<ProjectSetSettings>
{
    private readonly ILogger<ProjectSetCommand> _logger;

    public ProjectSetCommand(ILogger<ProjectSetCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectSetSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var match = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                _logger.LogError("Project not found: {Name}", settings.Name);
                return 1;
            }

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
                    _logger.LogError("Unknown field: {Field}. Valid fields: name, color, context", settings.Field);
                    return 1;
            }

            config.SaveSettings();
            _logger.LogInformation("Updated project {Field} to '{Value}'", settings.Field, settings.Value);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update project");
            return 1;
        }
    }
}

public class ProjectAddRepoCommand : Command<ProjectAddRepoSettings>
{
    private readonly ILogger<ProjectAddRepoCommand> _logger;

    public ProjectAddRepoCommand(ILogger<ProjectAddRepoCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectAddRepoSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var project = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                _logger.LogError("Project not found: {Name}", settings.ProjectName);
                return 1;
            }

            if (project.GetRepoRef(settings.RepoPath) != null)
            {
                _logger.LogError("Repository already exists in project: {Path}", settings.RepoPath);
                return 1;
            }

            project.Repos.Add(new RepoRef
            {
                Path = settings.RepoPath,
                PrRule = settings.PrRule ?? "default",
                BaseBranch = settings.BaseBranch,
                SyncStrategy = settings.SyncStrategy ?? "fetch"
            });

            config.SaveSettings();
            _logger.LogInformation("Added repository: {Path}", settings.RepoPath);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add repository to project");
            return 1;
        }
    }
}

public class ProjectRemoveRepoCommand : Command<ProjectRemoveRepoSettings>
{
    private readonly ILogger<ProjectRemoveRepoCommand> _logger;

    public ProjectRemoveRepoCommand(ILogger<ProjectRemoveRepoCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectRemoveRepoSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var project = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                _logger.LogError("Project not found: {Name}", settings.ProjectName);
                return 1;
            }

            var match = project.GetRepoRef(settings.RepoPath);
            if (match == null)
            {
                _logger.LogError("Repository not found in project: {Path}", settings.RepoPath);
                return 1;
            }

            project.Repos.Remove(match);
            config.SaveSettings();
            _logger.LogInformation("Removed repository: {Path}", settings.RepoPath);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove repository from project");
            return 1;
        }
    }
}

public class ProjectAddVerificationCommand : Command<ProjectAddVerificationSettings>
{
    private readonly ILogger<ProjectAddVerificationCommand> _logger;

    public ProjectAddVerificationCommand(ILogger<ProjectAddVerificationCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectAddVerificationSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var project = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                _logger.LogError("Project not found: {Name}", settings.ProjectName);
                return 1;
            }

            if (project.Verifications.Any(v => v.Name.Equals(settings.VerificationName, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogError("Verification already exists in project: {Name}", settings.VerificationName);
                return 1;
            }

            project.Verifications.Add(new ProjectVerificationRef
            {
                Name = settings.VerificationName,
                Required = settings.Required
            });

            config.SaveSettings();
            _logger.LogInformation("Added verification: {Name}", settings.VerificationName);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add verification to project");
            return 1;
        }
    }
}

public class ProjectRemoveVerificationCommand : Command<ProjectRemoveVerificationSettings>
{
    private readonly ILogger<ProjectRemoveVerificationCommand> _logger;

    public ProjectRemoveVerificationCommand(ILogger<ProjectRemoveVerificationCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectRemoveVerificationSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var project = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                _logger.LogError("Project not found: {Name}", settings.ProjectName);
                return 1;
            }

            var match = project.Verifications
                .FirstOrDefault(v => v.Name.Equals(settings.VerificationName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                _logger.LogError("Verification not found in project: {Name}", settings.VerificationName);
                return 1;
            }

            project.Verifications.Remove(match);
            config.SaveSettings();
            _logger.LogInformation("Removed verification: {Name}", settings.VerificationName);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove verification from project");
            return 1;
        }
    }
}

public class ProjectAddBuildDepCommand : Command<ProjectAddBuildDepSettings>
{
    private readonly ILogger<ProjectAddBuildDepCommand> _logger;

    public ProjectAddBuildDepCommand(ILogger<ProjectAddBuildDepCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectAddBuildDepSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var project = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                _logger.LogError("Project not found: {Name}", settings.ProjectName);
                return 1;
            }

            if (project.BuildDependencies.Contains(settings.Dependency, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogError("Build dependency already exists: {Dependency}", settings.Dependency);
                return 1;
            }

            project.BuildDependencies.Add(settings.Dependency);
            config.SaveSettings();
            _logger.LogInformation("Added build dependency: {Dependency}", settings.Dependency);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add build dependency to project");
            return 1;
        }
    }
}

public class ProjectRemoveBuildDepCommand : Command<ProjectRemoveBuildDepSettings>
{
    private readonly ILogger<ProjectRemoveBuildDepCommand> _logger;

    public ProjectRemoveBuildDepCommand(ILogger<ProjectRemoveBuildDepCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectRemoveBuildDepSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var project = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                _logger.LogError("Project not found: {Name}", settings.ProjectName);
                return 1;
            }

            var removed = project.BuildDependencies.RemoveAll(d => d.Equals(settings.Dependency, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                _logger.LogError("Build dependency not found: {Dependency}", settings.Dependency);
                return 1;
            }

            config.SaveSettings();
            _logger.LogInformation("Removed build dependency: {Dependency}", settings.Dependency);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove build dependency from project");
            return 1;
        }
    }
}

public class ProjectAddReviewActionCommand : Command<ProjectAddReviewActionSettings>
{
    private readonly ILogger<ProjectAddReviewActionCommand> _logger;

    public ProjectAddReviewActionCommand(ILogger<ProjectAddReviewActionCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectAddReviewActionSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var project = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                _logger.LogError("Project not found: {Name}", settings.ProjectName);
                return 1;
            }

            if (project.ReviewActions.Any(r => r.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogError("Review action already exists: {Name}", settings.Name);
                return 1;
            }

            project.ReviewActions.Add(new ReviewActionConfig
            {
                Name = settings.Name,
                Command = settings.Command ?? "",
                Condition = settings.Condition ?? ""
            });

            config.SaveSettings();
            _logger.LogInformation("Added review action: {Name}", settings.Name);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add review action to project");
            return 1;
        }
    }
}

public class ProjectRemoveReviewActionCommand : Command<ProjectRemoveReviewActionSettings>
{
    private readonly ILogger<ProjectRemoveReviewActionCommand> _logger;

    public ProjectRemoveReviewActionCommand(ILogger<ProjectRemoveReviewActionCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, ProjectRemoveReviewActionSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConfigService();
            var project = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                _logger.LogError("Project not found: {Name}", settings.ProjectName);
                return 1;
            }

            var match = project.ReviewActions
                .FirstOrDefault(r => r.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                _logger.LogError("Review action not found: {Name}", settings.Name);
                return 1;
            }

            project.ReviewActions.Remove(match);
            config.SaveSettings();
            _logger.LogInformation("Removed review action: {Name}", settings.Name);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove review action from project");
            return 1;
        }
    }
}
