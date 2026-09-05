using System.ComponentModel;
using System.Text.Json;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

internal static class VaultCommandHelpers
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static bool TryParseBool(string? value, out bool result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = false;
            return false;
        }

        var clean = value.Trim().ToLowerInvariant();
        if (clean is "true" or "1" or "yes" or "y")
        {
            result = true;
            return true;
        }
        if (clean is "false" or "0" or "no" or "n")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }
}

// --- Settings ---

public class VaultListSettings : CommandSettings
{
    [Description("Output as JSON")]
    [CommandOption("--json")]
    public bool Json { get; init; }
}

public class VaultStatusSettings : CommandSettings
{
    [Description("Vault ID or Name")]
    [CommandArgument(0, "[vault-id]")]
    public string? VaultId { get; set; }

    [Description("Output as JSON")]
    [CommandOption("--json")]
    public bool Json { get; init; }
}

public class VaultDiscoverSettings : CommandSettings
{
    [Description("Output as JSON")]
    [CommandOption("--json")]
    public bool Json { get; init; }
}

public class VaultConnectSettings : CommandSettings
{
    [Description("GitHub repository URL or owner/repo slug")]
    [CommandArgument(0, "<repo-url>")]
    public string RepoUrl { get; set; } = "";

    [Description("Custom display name for the vault")]
    [CommandOption("-n|--name")]
    public string? Name { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(RepoUrl, "repo-url");
    }
}

public class VaultCreateSettings : CommandSettings
{
    [Description("Name of the repository to create on GitHub")]
    [CommandArgument(0, "<repo-name>")]
    public string RepoName { get; set; } = "";

    [Description("Create public repository (default is private)")]
    [CommandOption("--public")]
    public bool Public { get; set; }

    [Description("Organization to create repository under")]
    [CommandOption("--org")]
    public string? Org { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(RepoName, "repo-name");
    }
}

public class VaultDisconnectSettings : CommandSettings
{
    [Description("Vault ID or Name")]
    [CommandArgument(0, "[vault-id]")]
    public string? VaultId { get; set; }
}

public class VaultSyncSettings : CommandSettings
{
    [Description("Vault ID or Name")]
    [CommandArgument(0, "[vault-id]")]
    public string? VaultId { get; set; }
}

public class VaultSetAutoSyncSettings : CommandSettings
{
    [Description("Enable (true/1/yes) or disable (false/0/no) auto-sync")]
    [CommandArgument(0, "<enabled>")]
    public string Enabled { get; set; } = "";

    [Description("Vault ID or Name")]
    [CommandOption("--vault")]
    public string? VaultId { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Enabled))
            return Spectre.Console.ValidationResult.Error("<enabled> is required and cannot be empty.");

        if (!VaultCommandHelpers.TryParseBool(Enabled, out _))
            return Spectre.Console.ValidationResult.Error($"Invalid boolean value '{Enabled}'. Valid values: true, false, 1, 0, yes, no.");

        return Spectre.Console.ValidationResult.Success();
    }
}

public class VaultCatalogSettings : CommandSettings
{
    [Description("Vault ID or Name")]
    [CommandArgument(0, "[vault-id]")]
    public string? VaultId { get; set; }

    [Description("Output as JSON")]
    [CommandOption("--json")]
    public bool Json { get; init; }
}

public class VaultImportSettings : CommandSettings
{
    [Description("Name of the project in the vault")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Target local project name (if different from vault project name)")]
    [CommandOption("--target-name")]
    public string? TargetName { get; set; }

    [Description("Vault ID or Name")]
    [CommandOption("--vault")]
    public string? VaultId { get; set; }

    [Description("Repository mapping in format <repoName>=<localPath>")]
    [CommandOption("--repo")]
    public string[]? Repos { get; set; }

    [Description("Do not import permission rules from the vault")]
    [CommandOption("--no-permissions")]
    public bool NoPermissions { get; set; }

    [Description("Merge into existing local project instead of replacing")]
    [CommandOption("--merge")]
    public bool Merge { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        var nameValidation = CliValidation.RequireNonEmpty(ProjectName, "project-name");
        if (!nameValidation.Successful)
            return nameValidation;

        if (Repos != null)
        {
            foreach (var r in Repos)
            {
                if (string.IsNullOrWhiteSpace(r) || !r.Contains('=') || r.StartsWith('=') || r.EndsWith('='))
                    return Spectre.Console.ValidationResult.Error($"Invalid repo mapping format '{r}'. Expected '<repoName>=<localPath>'.");
            }
        }

        return Spectre.Console.ValidationResult.Success();
    }
}

public class VaultPushSettings : CommandSettings
{
    [Description("Projects to publish to the vault")]
    [CommandArgument(0, "<projects>")]
    public string[] Projects { get; set; } = [];

    [Description("Target vault ID or Name")]
    [CommandOption("--vault")]
    public string? VaultId { get; set; }

    [Description("Version number (defaults to timestamp)")]
    [CommandOption("--version")]
    public string? Version { get; set; }

    [Description("Changelog description")]
    [CommandOption("--changelog")]
    public string? Changelog { get; set; }

    [Description("Pull request title")]
    [CommandOption("--title")]
    public string? Title { get; set; }

    [Description("Pull request body")]
    [CommandOption("--body")]
    public string? Body { get; set; }

    [Description("Pull request reviewers")]
    [CommandOption("--reviewer")]
    public string[]? Reviewers { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        if (Projects == null || Projects.Length == 0 || Projects.All(string.IsNullOrWhiteSpace))
            return Spectre.Console.ValidationResult.Error("At least one project name must be specified.");

        return Spectre.Console.ValidationResult.Success();
    }
}

public class VaultDeleteSettings : CommandSettings
{
    [Description("Name of the project to delete from the vault")]
    [CommandArgument(0, "<project-name>")]
    public string ProjectName { get; set; } = "";

    [Description("Vault ID or Name")]
    [CommandOption("--vault")]
    public string? VaultId { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(ProjectName, "project-name");
    }
}

// --- Commands ---

public class VaultListCommand(IVaultService vaultService) : AsyncCommand<VaultListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultListSettings settings, CancellationToken cancellationToken)
    {
        var vaults = await vaultService.GetVaultsAsync();

        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(vaults, VaultCommandHelpers.JsonOptions));
            return 0;
        }

        if (vaults.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No vaults found.[/]");
            return 0;
        }

        var rows = vaults.Select(v => (IReadOnlyList<string>)new[]
        {
            v.Id,
            v.Name,
            v.RepoUrl,
            v.CurrentBranch,
            v.CommitsAhead.ToString(),
            v.CommitsBehind.ToString(),
            v.LastSyncedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never",
            v.AlwaysUpToDate ? "Yes" : "No"
        });

        CliOutput.WriteTable(["Id", "Name", "Repository", "Branch", "Ahead", "Behind", "Last Synced", "Auto Sync"], rows);
        return 0;
    }
}

public class VaultStatusCommand(IVaultService vaultService) : AsyncCommand<VaultStatusSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultStatusSettings settings, CancellationToken cancellationToken)
    {
        var status = await vaultService.GetStatusAsync(settings.VaultId);

        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(status, VaultCommandHelpers.JsonOptions));
            return 0;
        }

        AnsiConsole.MarkupLine($"[bold]Vault:[/] {status.Name.EscapeMarkup()} ({status.Id.EscapeMarkup()})");
        AnsiConsole.MarkupLine($"  Configured: {(status.IsConfigured ? "[green]Yes[/]" : "[yellow]No[/]")}");
        AnsiConsole.MarkupLine($"  Repository: {status.RepoUrl.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  Local Path: {status.LocalPath.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  Branch: {status.CurrentBranch.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  Latest Commit: {status.LatestCommit?.EscapeMarkup() ?? "-"}");
        AnsiConsole.MarkupLine($"  Commits Ahead: {status.CommitsAhead}");
        AnsiConsole.MarkupLine($"  Commits Behind: {status.CommitsBehind}");
        AnsiConsole.MarkupLine($"  Last Synced: {(status.LastSyncedAt.HasValue ? status.LastSyncedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Never")}");
        AnsiConsole.MarkupLine($"  Auto Sync: {(status.AlwaysUpToDate ? "Enabled" : "Disabled")}");
        return 0;
    }
}

public class VaultDiscoverCommand(IVaultService vaultService) : AsyncCommand<VaultDiscoverSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultDiscoverSettings settings, CancellationToken cancellationToken)
    {
        var repos = await vaultService.DiscoverExistingVaultsAsync();

        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(repos, VaultCommandHelpers.JsonOptions));
            return 0;
        }

        if (repos.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No vault repositories discovered.[/]");
            return 0;
        }

        var rows = repos.Select(r => (IReadOnlyList<string>)new[]
        {
            r.FullName,
            r.RepoUrl,
            r.Owner,
            r.AccountType,
            r.IsPrivate ? "Private" : "Public"
        });

        CliOutput.WriteTable(["Full Name", "Repo URL", "Owner", "Account Type", "Visibility"], rows);
        return 0;
    }
}

public class VaultConnectCommand(IVaultService vaultService) : AsyncCommand<VaultConnectSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultConnectSettings settings, CancellationToken cancellationToken)
    {
        var result = await vaultService.ConnectVaultAsync(settings.RepoUrl.Trim(), settings.Name?.Trim());
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]{result.Message.EscapeMarkup()}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {(result.ErrorMessage ?? result.Message).EscapeMarkup()}");
        return 1;
    }
}

public class VaultCreateCommand(IVaultService vaultService) : AsyncCommand<VaultCreateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultCreateSettings settings, CancellationToken cancellationToken)
    {
        var result = await vaultService.CreateVaultRepoAsync(settings.RepoName.Trim(), isPrivate: !settings.Public, org: settings.Org?.Trim());
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]{result.Message.EscapeMarkup()}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {(result.ErrorMessage ?? result.Message).EscapeMarkup()}");
        return 1;
    }
}

public class VaultDisconnectCommand(IVaultService vaultService) : AsyncCommand<VaultDisconnectSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultDisconnectSettings settings, CancellationToken cancellationToken)
    {
        var result = await vaultService.DisconnectVaultAsync(settings.VaultId);
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]{result.Message.EscapeMarkup()}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {(result.ErrorMessage ?? result.Message).EscapeMarkup()}");
        return 1;
    }
}

public class VaultSyncCommand(IVaultService vaultService) : AsyncCommand<VaultSyncSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultSyncSettings settings, CancellationToken cancellationToken)
    {
        var result = await vaultService.PullLatestAsync(settings.VaultId);
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]{result.Message.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine($"Updated projects: {result.UpdatedProjectsCount}");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {(result.ErrorMessage ?? result.Message).EscapeMarkup()}");
        return 1;
    }
}

public class VaultSetAutoSyncCommand(IVaultService vaultService) : AsyncCommand<VaultSetAutoSyncSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultSetAutoSyncSettings settings, CancellationToken cancellationToken)
    {
        VaultCommandHelpers.TryParseBool(settings.Enabled, out var enabledBool);
        var result = await vaultService.SetAlwaysUpToDateAsync(enabledBool, settings.VaultId);
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]{result.Message.EscapeMarkup()}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {(result.ErrorMessage ?? result.Message).EscapeMarkup()}");
        return 1;
    }
}

public class VaultCatalogCommand(IVaultService vaultService) : AsyncCommand<VaultCatalogSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultCatalogSettings settings, CancellationToken cancellationToken)
    {
        var catalog = await vaultService.GetCatalogAsync(settings.VaultId);

        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(catalog, VaultCommandHelpers.JsonOptions));
            return 0;
        }

        if (catalog.Projects.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No projects found in vault catalog.[/]");
            return 0;
        }

        var rows = catalog.Projects.Select(p => (IReadOnlyList<string>)new[]
        {
            p.Name,
            p.RemoteVersion,
            p.SyncStatus.ToString(),
            p.ReposCount.ToString(),
            p.SkillsCount.ToString(),
            p.McpsCount.ToString(),
            p.MemoriesCount.ToString(),
            p.ReviewActionsCount.ToString(),
            p.VerificationsCount.ToString()
        });

        CliOutput.WriteTable(["Project", "Version", "Sync Status", "Repos", "Skills", "MCPs", "Memories", "Actions", "Verifications"], rows);
        return 0;
    }
}

public class VaultImportCommand(IVaultService vaultService, IConfigService config) : AsyncCommand<VaultImportSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultImportSettings settings, CancellationToken cancellationToken)
    {
        var repoMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (settings.Repos != null)
        {
            foreach (var r in settings.Repos)
            {
                var parts = r.Split('=', 2);
                if (parts.Length == 2)
                {
                    repoMappings[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }

        var request = new VaultImportRequest
        {
            SourceVaultId = settings.VaultId,
            ProjectName = settings.ProjectName.Trim(),
            TargetLocalProjectName = settings.TargetName?.Trim(),
            LocalRepoMappings = repoMappings,
            ImportPermissions = !settings.NoPermissions
        };

        var result = settings.Merge
            ? await vaultService.MergeProjectAsync(request, settings.VaultId)
            : await vaultService.ImportProjectAsync(request, settings.VaultId);

        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]{result.Message.EscapeMarkup()}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {(result.ErrorMessage ?? result.Message).EscapeMarkup()}");
        return 1;
    }
}

public class VaultPushCommand(IVaultService vaultService, IConfigService config) : AsyncCommand<VaultPushSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultPushSettings settings, CancellationToken cancellationToken)
    {
        var availableProjects = config.Settings.Projects.Select(p => p.Name).ToList();
        foreach (var projName in settings.Projects)
        {
            if (!availableProjects.Contains(projName, StringComparer.OrdinalIgnoreCase))
            {
                CliValidation.ThrowProjectNotFound(projName, availableProjects);
            }
        }

        var projectList = settings.Projects.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var version = !string.IsNullOrWhiteSpace(settings.Version)
            ? settings.Version.Trim()
            : vaultService.GenerateVersionTimestamp();

        var changelog = settings.Changelog?.Trim() ?? "";
        var prTitle = !string.IsNullOrWhiteSpace(settings.Title)
            ? settings.Title.Trim()
            : $"feat(vault): update {string.Join(", ", projectList)} to v{version}";

        var prBody = !string.IsNullOrWhiteSpace(settings.Body)
            ? settings.Body.Trim()
            : $"### Vault Version Update: v{version}\n\n**Changelog:**\n{changelog}\n\n**Projects Included:**\n{string.Join("\n", projectList.Select(p => $"- {p}"))}\n\n> Published from Ivy Tendril CLI.";

        var reviewers = settings.Reviewers?.SelectMany(r => r.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct().ToList() ?? new List<string>();

        var selectedSkills = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var selectedMcps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var selectedMemories = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var selectedReviewActions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var selectedVerifications = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var syncPermissions = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var projName in projectList)
        {
            var assets = await vaultService.CollectProjectAssetsAsync(projName);
            selectedSkills[projName] = assets.Skills;
            selectedMcps[projName] = assets.McpServers;
            selectedMemories[projName] = assets.Memories;
            selectedReviewActions[projName] = assets.ReviewActions;
            selectedVerifications[projName] = assets.Verifications;
            syncPermissions[projName] = true;
        }

        var request = new VaultExportRequest
        {
            TargetVaultId = settings.VaultId,
            ProjectNames = projectList,
            Version = version,
            Changelog = changelog,
            PrTitle = prTitle,
            PrBody = prBody,
            Reviewers = reviewers,
            SelectedSkills = selectedSkills,
            SelectedMcps = selectedMcps,
            SelectedMemories = selectedMemories,
            SelectedReviewActions = selectedReviewActions,
            SelectedVerifications = selectedVerifications,
            SyncPermissions = syncPermissions
        };

        var result = await vaultService.PushAndCreatePrAsync(request, settings.VaultId);
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]Pull request created successfully![/]");
            if (!string.IsNullOrEmpty(result.PrUrl))
                AnsiConsole.MarkupLine($"PR URL: {result.PrUrl.EscapeMarkup()}");
            if (!string.IsNullOrEmpty(result.BranchName))
                AnsiConsole.MarkupLine($"Branch: {result.BranchName.EscapeMarkup()}");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {(result.ErrorMessage ?? "Failed to create PR for vault update.").EscapeMarkup()}");
        return 1;
    }
}

public class VaultDeleteCommand(IVaultService vaultService) : AsyncCommand<VaultDeleteSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultDeleteSettings settings, CancellationToken cancellationToken)
    {
        var result = await vaultService.DeleteProjectFromVaultAsync(settings.ProjectName.Trim(), settings.VaultId);
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]Project '{settings.ProjectName}' deleted from vault.[/]");
            if (!string.IsNullOrEmpty(result.PrUrl))
                AnsiConsole.MarkupLine($"PR URL: {result.PrUrl.EscapeMarkup()}");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {(result.ErrorMessage ?? "Failed to delete project from vault.").EscapeMarkup()}");
        return 1;
    }
}
