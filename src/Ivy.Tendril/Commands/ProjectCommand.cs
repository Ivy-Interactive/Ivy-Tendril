using System.ComponentModel;
using Ivy.Tendril.Helpers;
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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.ValidateProjectName(Name)
        );
    }
}

public class ProjectGetSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(Name, "name");
    }
}

public class ProjectRemoveSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(Name, "name");
    }
}

public class ProjectSetSettings : CommandSettings
{
    private static readonly string[] ValidFields = ["name", "color", "context", "stackhash"];

    [Description("Project name")]
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = "";

    [Description("Field name (name, color, context, stackHash)")]
    [CommandArgument(1, "<field>")]
    public string Field { get; set; } = "";

    [Description("Field value")]
    [CommandArgument(2, "<value>")]
    public string Value { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.ValidateField(Field, ValidFields),
            string.Equals(Field, "name", StringComparison.OrdinalIgnoreCase)
                ? CliValidation.ValidateProjectName(Value)
                : Spectre.Console.ValidationResult.Success()
        );
    }
}

public class ProjectAddRepoSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Repository path")]
    [CommandArgument(1, "<repo-path>")]
    public string RepoPath { get; set; } = "";

    [CommandOption("--base-branch")]
    [Description("Base branch name")]
    public string? BaseBranch { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(RepoPath, "repo-path")
        );
    }
}

public class ProjectRemoveRepoSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Repository path")]
    [CommandArgument(1, "<repo-path>")]
    public string RepoPath { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(RepoPath, "repo-path"));
    }
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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(VerificationName, "verification-name"));
    }
}

public class ProjectRemoveVerificationSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Verification name")]
    [CommandArgument(1, "<verification-name>")]
    public string VerificationName { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(VerificationName, "verification-name"));
    }
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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(VerificationName, "verification-name"));
    }
}

public class ProjectAddBuildDepSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Build dependency")]
    [CommandArgument(1, "<dependency>")]
    public string Dependency { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Dependency, "dependency"));
    }
}

public class ProjectRemoveBuildDepSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Build dependency")]
    [CommandArgument(1, "<dependency>")]
    public string Dependency { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Dependency, "dependency"));
    }
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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Name, "name"));
    }
}

public class ProjectRemoveReviewActionSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Review action name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Name, "name"));
    }
}

public class ProjectListMcpSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(ProjectName, "project-name");
    }
}

public class ProjectAddMcpSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("MCP server name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";

    [Description("Command executable (e.g. npx)")]
    [CommandArgument(2, "<command>")]
    public string Command { get; set; } = "";

    [CommandOption("--arg <argument>")]
    [Description("Command argument (can be specified multiple times)")]
    public string[]? Arguments { get; set; }

    [CommandOption("--env <key=value>")]
    [Description("Environment variable in KEY=VALUE format (can be specified multiple times)")]
    public string[]? Environment { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.RequireNonEmpty(Command, "command"));
    }
}

public class ProjectRemoveMcpSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("MCP server name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Name, "name"));
    }
}

public class ProjectListSkillsSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(ProjectName, "project-name");
    }
}

public class ProjectAddSkillSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Skill name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";

    [CommandOption("--description <description>")]
    [Description("Skill description")]
    public string? Description { get; set; }

    [CommandOption("--path <path>")]
    [Description("Path to skill folder/file")]
    public string? Path { get; set; }

    [CommandOption("--instructions <instructions>")]
    [Description("Inline skill instructions")]
    public string? Instructions { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Name, "name"));
    }
}

public class ProjectRemoveSkillSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Skill name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Name, "name"));
    }
}

public class ProjectImportSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Repository path or repository name in project")]
    [CommandArgument(1, "<repo>")]
    public string Repo { get; set; } = "";

    [CommandOption("--mcp-only")]
    [Description("Only import MCP servers")]
    public bool McpOnly { get; set; }

    [CommandOption("--skills-only")]
    [Description("Only import custom skills")]
    public bool SkillsOnly { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Repo, "repo"));
    }
}

public class ProjectImportMcpSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Repository path or repository name in project")]
    [CommandArgument(1, "<repo>")]
    public string Repo { get; set; } = "";

    [CommandOption("--name <name>")]
    [Description("Specific MCP server name to import")]
    public string? Name { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Repo, "repo"));
    }
}

public class ProjectImportSkillsSettings : CommandSettings
{
    [Description("Project name")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Repository path or repository name in project")]
    [CommandArgument(1, "<repo>")]
    public string Repo { get; set; } = "";

    [CommandOption("--name <name>")]
    [Description("Specific skill name to import")]
    public string? Name { get; set; }

    [CommandOption("--no-copy")]
    [Description("Do not copy skill directory files into project Skills/ folder")]
    public bool NoCopy { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(ProjectName, "project-name"),
            CliValidation.RequireNonEmpty(Repo, "repo"));
    }
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
            CliValidation.ThrowProjectNotFound(settings.Name, config.Settings.Projects.Select(p => p.Name));

        AnsiConsole.MarkupLine($"[bold]{project.Name.EscapeMarkup()}[/]");
        if (!string.IsNullOrEmpty(project.Color))
            AnsiConsole.MarkupLine($"  Color: {project.Color.EscapeMarkup()}");
        if (!string.IsNullOrEmpty(project.Context))
            AnsiConsole.MarkupLine($"  Context: {project.Context.EscapeMarkup()}");
        if (!string.IsNullOrEmpty(project.StackHash))
            AnsiConsole.MarkupLine($"  StackHash: {project.StackHash.EscapeMarkup()}");

        if (project.Repos.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Repositories[/]");
            var repoRows = project.Repos.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Path,
                r.BaseBranch ?? "-"
            });
            CliOutput.WriteTable(["Path", "Base Branch"], repoRows);
        }

        if (project.Verifications.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Verifications[/]");
            var verRows = project.Verifications.Select(v => (IReadOnlyList<string>)new[]
            {
                v.Name,
                v.Required ? "Yes" : "No"
            });
            CliOutput.WriteTable(["Name", "Required"], verRows);
        }

        if (project.ReviewActions.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Review Actions[/]");
            var raRows = project.ReviewActions.Select(ra => (IReadOnlyList<string>)new[]
            {
                ra.Name,
                ra.Command,
                ra.Condition
            });
            CliOutput.WriteTable(["Name", "Command", "Condition"], raRows);
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

        config.MutateAndSave(s =>
        {
            if (s.Projects.Any(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Project already exists: {settings.Name}");

            s.Projects.Add(new ProjectConfig
            {
                Name = settings.Name,
                Color = settings.Color ?? "",
                Context = settings.Context ?? ""
            });
        });

        Console.WriteLine($"Added project: {settings.Name}");
        return 0;
    }
}

public class ProjectRemoveCommand : Command<ProjectRemoveSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var match = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                CliValidation.ThrowProjectNotFound(settings.Name, s.Projects.Select(p => p.Name));

            s.Projects.Remove(match);
        });

        Console.WriteLine($"Removed project: {settings.Name}");
        return 0;
    }
}

public class ProjectSetCommand : Command<ProjectSetSettings>
{
    protected override int Execute(CommandContext context, ProjectSetSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var match = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                CliValidation.ThrowProjectNotFound(settings.Name, s.Projects.Select(p => p.Name));

            switch (settings.Field.ToLower())
            {
                case "name":
                    var nameError = InputSanitizer.DescribeProjectNameError(settings.Value);
                    if (nameError != null)
                        throw new InvalidOperationException(nameError);
                    match.Name = settings.Value;
                    break;
                case "color":
                    match.Color = settings.Value;
                    break;
                case "context":
                    match.Context = settings.Value;
                    break;
                case "stackhash":
                    match.StackHash = settings.Value;
                    break;
                default:
                    throw new ArgumentException($"Unknown field: {settings.Field}. Valid fields: name, color, context, stackHash");
            }
        });

        Console.WriteLine($"Updated project {settings.Field} to '{settings.Value}'");
        return 0;
    }
}

public class ProjectAddRepoCommand : Command<ProjectAddRepoSettings>
{
    protected override int Execute(CommandContext context, ProjectAddRepoSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        // Fail fast on an unknown project before shelling out to git below. The authoritative
        // lookup happens inside MutateAndSave, against the settings graph that gets written.
        if (!config.Settings.Projects.Any(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase)))
            CliValidation.ThrowProjectNotFound(settings.ProjectName, config.Settings.Projects.Select(p => p.Name));

        var repoPath = settings.RepoPath;
        var kind = RepoPathValidator.Classify(repoPath);
        if (kind != RepoPathKind.LocalPath)
        {
            var tendrilHome = config.TendrilHome;
            var owner = RepoPathValidator.ExtractOwnerName(repoPath) ?? "default";
            var repoName = RepoPathValidator.ExtractRepoName(repoPath) ?? Guid.NewGuid().ToString();
            var destPath = ProjectPathHelper.GetRepoPath(tendrilHome, settings.ProjectName, owner, repoName);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            if (!Directory.Exists(destPath))
            {
                var success = ProcessCheckHelper.CloneRepositoryAsync(repoPath, destPath).GetAwaiter().GetResult();
                if (!success)
                    throw new InvalidOperationException($"Failed to clone repository from URL: {repoPath}");
            }
            repoPath = destPath;
        }

        // Resolve the branch before taking the config lock: these spawn git and can be slow.
        string? baseBranch = settings.BaseBranch;
        if (!string.IsNullOrWhiteSpace(baseBranch))
        {
            var isValid = Ivy.Tendril.Helpers.GitHelper.IsValidBranchAsync(repoPath, baseBranch, config.TendrilHome).GetAwaiter().GetResult();
            if (!isValid)
                throw new InvalidOperationException($"Branch '{baseBranch}' does not exist in repository: {repoPath}");
        }
        else
        {
            // No branch supplied, so detect and persist the repo's real default branch.
            baseBranch = Ivy.Tendril.Helpers.GitHelper.ResolveDefaultBranch(repoPath, config.TendrilHome);
        }

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            if (project.GetRepoRef(repoPath) != null)
                throw new InvalidOperationException($"Repository already exists in project: {repoPath}");

            project.Repos.Add(new RepoRef
            {
                Path = repoPath,
                BaseBranch = baseBranch
            });
        });

        Console.WriteLine($"Added repository: {repoPath}");
        return 0;
    }
}

public class ProjectRemoveRepoCommand : Command<ProjectRemoveRepoSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveRepoSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            var match = project.GetRepoRef(settings.RepoPath);
            if (match == null)
                CliValidation.ThrowRepoNotFound(settings.RepoPath, project.Repos.Select(r => r.Path));

            project.Repos.Remove(match);
        });

        Console.WriteLine($"Removed repository: {settings.RepoPath}");
        return 0;
    }
}

public class ProjectAddVerificationCommand : Command<ProjectAddVerificationSettings>
{
    protected override int Execute(CommandContext context, ProjectAddVerificationSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

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
                    CliValidation.ThrowVerificationNotFound(settings.After, project.Verifications.Select(v => v.Name));
                project.Verifications.Insert(afterIndex + 1, newRef);
            }
            else
            {
                project.Verifications.Add(newRef);
            }
        });

        Console.WriteLine($"Added verification: {settings.VerificationName}");
        return 0;
    }
}

public class ProjectRemoveVerificationCommand : Command<ProjectRemoveVerificationSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveVerificationSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            var match = project.Verifications
                .FirstOrDefault(v => v.Name.Equals(settings.VerificationName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                CliValidation.ThrowVerificationNotFound(settings.VerificationName, project.Verifications.Select(v => v.Name));

            project.Verifications.Remove(match);
        });

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
        var insertIndex = 0;

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            var item = project.Verifications
                .FirstOrDefault(v => v.Name.Equals(settings.VerificationName, StringComparison.OrdinalIgnoreCase));

            if (item == null)
                CliValidation.ThrowVerificationNotFound(settings.VerificationName, project.Verifications.Select(v => v.Name));

            project.Verifications.Remove(item);

            if (settings.Before != null)
            {
                var targetIndex = project.Verifications
                    .FindIndex(v => v.Name.Equals(settings.Before, StringComparison.OrdinalIgnoreCase));
                if (targetIndex < 0)
                {
                    project.Verifications.Add(item);
                    CliValidation.ThrowVerificationTargetNotFound("before", settings.Before, project.Verifications.Select(v => v.Name));
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
                    CliValidation.ThrowVerificationTargetNotFound("after", settings.After, project.Verifications.Select(v => v.Name));
                }
                insertIndex = targetIndex + 1;
            }
            else
            {
                insertIndex = Math.Clamp(settings.Position!.Value, 0, project.Verifications.Count);
            }

            project.Verifications.Insert(insertIndex, item);
        });

        Console.WriteLine($"Moved verification '{settings.VerificationName}' to position {insertIndex}");
        return 0;
    }
}

public class ProjectAddBuildDepCommand : Command<ProjectAddBuildDepSettings>
{
    protected override int Execute(CommandContext context, ProjectAddBuildDepSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            if (project.BuildDependencies.Contains(settings.Dependency, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Build dependency already exists: {settings.Dependency}");

            project.BuildDependencies.Add(settings.Dependency);
        });

        Console.WriteLine($"Added build dependency: {settings.Dependency}");
        return 0;
    }
}

public class ProjectRemoveBuildDepCommand : Command<ProjectRemoveBuildDepSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveBuildDepSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            var removed = project.BuildDependencies.RemoveAll(d => d.Equals(settings.Dependency, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                CliValidation.ThrowBuildDependencyNotFound(settings.Dependency, project.BuildDependencies);
        });

        Console.WriteLine($"Removed build dependency: {settings.Dependency}");
        return 0;
    }
}

public class ProjectAddReviewActionCommand : Command<ProjectAddReviewActionSettings>
{
    protected override int Execute(CommandContext context, ProjectAddReviewActionSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            if (project.ReviewActions.Any(r => r.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Review action already exists: {settings.Name}");

            project.ReviewActions.Add(new ReviewActionConfig
            {
                Name = settings.Name,
                Command = settings.Command ?? "",
                Condition = settings.Condition ?? ""
            });
        });

        Console.WriteLine($"Added review action: {settings.Name}");
        return 0;
    }
}

public class ProjectRemoveReviewActionCommand : Command<ProjectRemoveReviewActionSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveReviewActionSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            var match = project.ReviewActions
                .FirstOrDefault(r => r.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                CliValidation.ThrowReviewActionNotFound(settings.Name, project.ReviewActions.Select(r => r.Name));

            project.ReviewActions.Remove(match);
        });

        Console.WriteLine($"Removed review action: {settings.Name}");
        return 0;
    }
}

internal static class ProjectRepoResolver
{
    public static string ResolveRepoPath(ProjectConfig project, string repoArg, string tendrilHome)
    {
        var matchingRepo = project.Repos.FirstOrDefault(r =>
            r.Path.Equals(repoArg, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(r.Path).Equals(repoArg, StringComparison.OrdinalIgnoreCase) ||
            r.Path.EndsWith("/" + repoArg, StringComparison.OrdinalIgnoreCase) ||
            r.Path.EndsWith("\\" + repoArg, StringComparison.OrdinalIgnoreCase));

        var target = matchingRepo != null ? matchingRepo.Path : repoArg;
        var (path, error) = RepoAssetScanner.ResolveAndPrepareRepoPath(target, tendrilHome);
        if (error != null || string.IsNullOrEmpty(path))
            throw new InvalidOperationException(error ?? $"Failed to resolve repository: {repoArg}");

        return path;
    }
}

public class ProjectListMcpCommand : Command<ProjectListMcpSettings>
{
    protected override int Execute(CommandContext context, ProjectListMcpSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            CliValidation.ThrowProjectNotFound(settings.ProjectName, config.Settings.Projects.Select(p => p.Name));

        if (project.McpServers.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No MCP servers configured for this project.[/]");
            return 0;
        }

        foreach (var server in project.McpServers)
        {
            var argsStr = server.Arguments.Count > 0 ? " " + string.Join(" ", server.Arguments) : "";
            AnsiConsole.MarkupLine($"[bold]{server.Name.EscapeMarkup()}[/]: {server.Command.EscapeMarkup()}{argsStr.EscapeMarkup()}");
        }

        return 0;
    }
}

public class ProjectAddMcpCommand : Command<ProjectAddMcpSettings>
{
    protected override int Execute(CommandContext context, ProjectAddMcpSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            if (project.McpServers.Any(m => m.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"MCP server already exists: {settings.Name}");

            var envDict = new Dictionary<string, string>();
            if (settings.Environment != null)
            {
                foreach (var envItem in settings.Environment)
                {
                    var parts = envItem.Split('=', 2);
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                        envDict[parts[0].Trim()] = parts[1].Trim();
                }
            }

            project.McpServers.Add(new ProjectMcpServerRef
            {
                Name = settings.Name,
                Command = settings.Command,
                Arguments = settings.Arguments != null ? settings.Arguments.ToList() : new List<string>(),
                Environment = envDict,
                Disabled = false
            });
        });

        Console.WriteLine($"Added MCP server: {settings.Name}");
        return 0;
    }
}

public class ProjectRemoveMcpCommand : Command<ProjectRemoveMcpSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveMcpSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            var match = project.McpServers
                .FirstOrDefault(m => m.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                throw new InvalidOperationException($"MCP server not found: {settings.Name}");

            project.McpServers.Remove(match);
        });

        Console.WriteLine($"Removed MCP server: {settings.Name}");
        return 0;
    }
}

public class ProjectListSkillsCommand : Command<ProjectListSkillsSettings>
{
    protected override int Execute(CommandContext context, ProjectListSkillsSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            CliValidation.ThrowProjectNotFound(settings.ProjectName, config.Settings.Projects.Select(p => p.Name));

        if (project.Skills.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No custom skills configured for this project.[/]");
            return 0;
        }

        foreach (var skill in project.Skills)
        {
            var desc = !string.IsNullOrEmpty(skill.Description) ? $" - {skill.Description}" : "";
            AnsiConsole.MarkupLine($"[bold]{skill.Name.EscapeMarkup()}[/]{desc.EscapeMarkup()}");
        }

        return 0;
    }
}

public class ProjectAddSkillCommand : Command<ProjectAddSkillSettings>
{
    protected override int Execute(CommandContext context, ProjectAddSkillSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            if (project.Skills.Any(sk => sk.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Skill already exists: {settings.Name}");

            project.Skills.Add(new ProjectSkillRef
            {
                Name = settings.Name,
                Description = settings.Description ?? "",
                Path = string.IsNullOrWhiteSpace(settings.Path) ? null : settings.Path,
                Instructions = string.IsNullOrWhiteSpace(settings.Instructions) ? null : settings.Instructions,
                Disabled = false
            });
        });

        Console.WriteLine($"Added custom skill: {settings.Name}");
        return 0;
    }
}

public class ProjectRemoveSkillCommand : Command<ProjectRemoveSkillSettings>
{
    protected override int Execute(CommandContext context, ProjectRemoveSkillSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();

        config.MutateAndSave(s =>
        {
            var project = s.Projects
                .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
                CliValidation.ThrowProjectNotFound(settings.ProjectName, s.Projects.Select(p => p.Name));

            var match = project.Skills
                .FirstOrDefault(sk => sk.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                throw new InvalidOperationException($"Skill not found: {settings.Name}");

            project.Skills.Remove(match);
        });

        Console.WriteLine($"Removed custom skill: {settings.Name}");
        return 0;
    }
}

public class ProjectImportCommand : Command<ProjectImportSettings>
{
    protected override int Execute(CommandContext context, ProjectImportSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            CliValidation.ThrowProjectNotFound(settings.ProjectName, config.Settings.Projects.Select(p => p.Name));

        var repoPath = ProjectRepoResolver.ResolveRepoPath(project, settings.Repo, config.TendrilHome);
        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Repository path not found: {repoPath}");

        var importMcp = !settings.SkillsOnly;
        var importSkills = !settings.McpOnly;

        var importedMcpCount = 0;
        var importedSkillCount = 0;

        config.MutateAndSave(s =>
        {
            var proj = s.Projects
                .First(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (importMcp)
            {
                var discoveredServers = RepoAssetScanner.ScanMcpServers(repoPath);
                foreach (var srv in discoveredServers)
                {
                    var existing = proj.McpServers.FirstOrDefault(m => m.Name.Equals(srv.Name, StringComparison.OrdinalIgnoreCase));
                    var mcpRef = RepoAssetScanner.ImportMcpServer(srv);
                    if (existing != null)
                    {
                        proj.McpServers[proj.McpServers.IndexOf(existing)] = mcpRef;
                    }
                    else
                    {
                        proj.McpServers.Add(mcpRef);
                    }
                    importedMcpCount++;
                }
            }

            if (importSkills)
            {
                var discoveredSkills = RepoAssetScanner.ScanSkills(repoPath);
                foreach (var sk in discoveredSkills)
                {
                    var existing = proj.Skills.FirstOrDefault(k => k.Name.Equals(sk.Name, StringComparison.OrdinalIgnoreCase));
                    var skillRef = RepoAssetScanner.ImportSkillToProject(config.TendrilHome, proj.Name, sk, copyFiles: true);
                    if (existing != null)
                    {
                        proj.Skills[proj.Skills.IndexOf(existing)] = skillRef;
                    }
                    else
                    {
                        proj.Skills.Add(skillRef);
                    }
                    importedSkillCount++;
                }
            }
        });

        AnsiConsole.MarkupLine($"[green]Import complete from:[/] {repoPath.EscapeMarkup()}");
        if (importMcp)
            AnsiConsole.MarkupLine($"  MCP servers imported/updated: [bold]{importedMcpCount}[/]");
        if (importSkills)
            AnsiConsole.MarkupLine($"  Custom skills imported/updated: [bold]{importedSkillCount}[/]");

        return 0;
    }
}

public class ProjectImportMcpCommand : Command<ProjectImportMcpSettings>
{
    protected override int Execute(CommandContext context, ProjectImportMcpSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            CliValidation.ThrowProjectNotFound(settings.ProjectName, config.Settings.Projects.Select(p => p.Name));

        var repoPath = ProjectRepoResolver.ResolveRepoPath(project, settings.Repo, config.TendrilHome);
        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Repository path not found: {repoPath}");

        var discoveredServers = RepoAssetScanner.ScanMcpServers(repoPath);
        if (!string.IsNullOrWhiteSpace(settings.Name))
        {
            discoveredServers = discoveredServers
                .Where(s => s.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (discoveredServers.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No matching MCP servers found in repository.[/]");
            return 0;
        }

        config.MutateAndSave(s =>
        {
            var proj = s.Projects
                .First(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            foreach (var srv in discoveredServers)
            {
                var existing = proj.McpServers.FirstOrDefault(m => m.Name.Equals(srv.Name, StringComparison.OrdinalIgnoreCase));
                var mcpRef = RepoAssetScanner.ImportMcpServer(srv);
                if (existing != null)
                {
                    proj.McpServers[proj.McpServers.IndexOf(existing)] = mcpRef;
                }
                else
                {
                    proj.McpServers.Add(mcpRef);
                }
            }
        });

        foreach (var srv in discoveredServers)
            AnsiConsole.MarkupLine($"[green]Imported MCP server:[/] [bold]{srv.Name.EscapeMarkup()}[/] ({srv.Command.EscapeMarkup()})");

        return 0;
    }
}

public class ProjectImportSkillsCommand : Command<ProjectImportSkillsSettings>
{
    protected override int Execute(CommandContext context, ProjectImportSkillsSettings settings, CancellationToken cancellationToken)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            CliValidation.ThrowProjectNotFound(settings.ProjectName, config.Settings.Projects.Select(p => p.Name));

        var repoPath = ProjectRepoResolver.ResolveRepoPath(project, settings.Repo, config.TendrilHome);
        if (!Directory.Exists(repoPath))
            throw new DirectoryNotFoundException($"Repository path not found: {repoPath}");

        var discoveredSkills = RepoAssetScanner.ScanSkills(repoPath);
        if (!string.IsNullOrWhiteSpace(settings.Name))
        {
            discoveredSkills = discoveredSkills
                .Where(s => s.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (discoveredSkills.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No matching skills found in repository.[/]");
            return 0;
        }

        config.MutateAndSave(s =>
        {
            var proj = s.Projects
                .First(p => p.Name.Equals(settings.ProjectName, StringComparison.OrdinalIgnoreCase));

            foreach (var sk in discoveredSkills)
            {
                var existing = proj.Skills.FirstOrDefault(k => k.Name.Equals(sk.Name, StringComparison.OrdinalIgnoreCase));
                var skillRef = RepoAssetScanner.ImportSkillToProject(config.TendrilHome, proj.Name, sk, copyFiles: !settings.NoCopy);
                if (existing != null)
                {
                    proj.Skills[proj.Skills.IndexOf(existing)] = skillRef;
                }
                else
                {
                    proj.Skills.Add(skillRef);
                }
            }
        });

        foreach (var sk in discoveredSkills)
            AnsiConsole.MarkupLine($"[green]Imported skill:[/] [bold]{sk.Name.EscapeMarkup()}[/] - {sk.Description.EscapeMarkup()}");

        return 0;
    }
}
