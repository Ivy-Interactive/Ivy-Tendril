using Ivy.Tendril.Commands;
using Ivy.Tendril.Infrastructure;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace Ivy.Tendril.Test.Commands;

public class FakeVaultService : IVaultService
{
    public string GenerateVersionTimestamp() => "2026.09.05.100000";

    public List<VaultStatus> VaultsToReturn { get; set; } = [];
    public VaultStatus StatusToReturn { get; set; } = new()
    {
        Id = "vault-1",
        Name = "Main Vault",
        IsConfigured = true,
        RepoUrl = "https://github.com/org/vault.git",
        LocalPath = "C:/vaults/main",
        CurrentBranch = "main",
        LatestCommit = "abc1234",
        CommitsAhead = 1,
        CommitsBehind = 2,
        LastSyncedAt = DateTimeOffset.UtcNow,
        AlwaysUpToDate = true
    };
    public VaultCatalog CatalogToReturn { get; set; } = new();
    public List<GitHubAccountOption> AccountsToReturn { get; set; } = [];
    public List<DiscoveredVaultRepo> DiscoveredVaultsToReturn { get; set; } = [];
    public VaultResult CreateResultToReturn { get; set; } = new(true, "Repository created successfully.");
    public VaultResult ConnectResultToReturn { get; set; } = new(true, "Vault connected successfully.");
    public VaultResult DisconnectResultToReturn { get; set; } = new(true, "Vault disconnected successfully.");
    public VaultResult AutoSyncResultToReturn { get; set; } = new(true, "Auto-sync updated.");
    public VaultPrResult PushResultToReturn { get; set; } = new(true, PrUrl: "https://github.com/org/vault/pull/1", BranchName: "vault-update");
    public VaultResult ImportResultToReturn { get; set; } = new(true, "Project imported successfully.");
    public VaultResult MergeResultToReturn { get; set; } = new(true, "Project merged successfully.");
    public VaultPrResult DeleteResultToReturn { get; set; } = new(true, PrUrl: "https://github.com/org/vault/pull/2");
    public VaultSyncResult PullResultToReturn { get; set; } = new(true, UpdatedProjectsCount: 2, Message: "Synced successfully.");

    public event Action? VaultChanged { add { } remove { } }

    public Task<List<VaultStatus>> GetVaultsAsync() => Task.FromResult(VaultsToReturn);
    public Task<VaultStatus> GetStatusAsync(string? vaultId = null) => Task.FromResult(StatusToReturn);
    public Task<VaultCatalog> GetCatalogAsync(string? vaultId = null) => Task.FromResult(CatalogToReturn);
    public Task<List<GitHubAccountOption>> GetGitHubAccountsAndOrgsAsync() => Task.FromResult(AccountsToReturn);
    public Task<List<DiscoveredVaultRepo>> DiscoverExistingVaultsAsync() => Task.FromResult(DiscoveredVaultsToReturn);
    public Task<VaultResult> CreateVaultRepoAsync(string repoName, bool isPrivate = true, string? org = null) => Task.FromResult(CreateResultToReturn);
    public Task<VaultResult> ConnectVaultAsync(string repoUrl, string? customName = null) => Task.FromResult(ConnectResultToReturn);
    public Task<VaultResult> DisconnectVaultAsync(string? vaultId = null) => Task.FromResult(DisconnectResultToReturn);
    public Task<VaultResult> SetAlwaysUpToDateAsync(bool alwaysUpToDate, string? vaultId = null) => Task.FromResult(AutoSyncResultToReturn);
    public Task<VaultPrResult> PushAndCreatePrAsync(VaultExportRequest request, string? vaultId = null) => Task.FromResult(PushResultToReturn);
    public Task<VaultResult> ImportProjectAsync(VaultImportRequest request, string? vaultId = null) => Task.FromResult(ImportResultToReturn);
    public Task<VaultResult> ImportProjectAsync(string projectName, Dictionary<string, string> localRepoMappings, string? vaultId = null) => Task.FromResult(ImportResultToReturn);
    public Task<VaultResult> MergeProjectAsync(VaultImportRequest request, string? vaultId = null) => Task.FromResult(MergeResultToReturn);
    public Task<VaultPrResult> DeleteProjectFromVaultAsync(string projectName, string? vaultId = null) => Task.FromResult(DeleteResultToReturn);
    public Task<VaultSyncResult> PullLatestAsync(string? vaultId = null) => Task.FromResult(PullResultToReturn);
}

public class VaultCommandSettingsValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void VaultConnectSettings_RejectsEmptyRepoUrl(string url)
    {
        var settings = new VaultConnectSettings { RepoUrl = url };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("repo-url", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VaultConnectSettings_ValidUrl_Succeeds()
    {
        var settings = new VaultConnectSettings { RepoUrl = "https://github.com/owner/vault.git" };
        var result = settings.Validate();
        Assert.True(result.Successful);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void VaultCreateSettings_RejectsEmptyRepoName(string name)
    {
        var settings = new VaultCreateSettings { RepoName = name };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("repo-name", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VaultCreateSettings_ValidName_Succeeds()
    {
        var settings = new VaultCreateSettings { RepoName = "my-vault" };
        var result = settings.Validate();
        Assert.True(result.Successful);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("maybe")]
    [InlineData("2")]
    [InlineData("")]
    public void VaultSetAutoSyncSettings_RejectsInvalidBoolean(string enabled)
    {
        var settings = new VaultSetAutoSyncSettings { Enabled = enabled };
        var result = settings.Validate();
        Assert.False(result.Successful);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("0")]
    [InlineData("no")]
    public void VaultSetAutoSyncSettings_AcceptsValidBoolean(string enabled)
    {
        var settings = new VaultSetAutoSyncSettings { Enabled = enabled };
        var result = settings.Validate();
        Assert.True(result.Successful);
    }

    [Fact]
    public void VaultImportSettings_ValidatesRepoMappingFormats()
    {
        var invalidSettings = new VaultImportSettings
        {
            ProjectName = "MyProject",
            Repos = ["no-equals-sign", "valid=path"]
        };
        var invalidResult = invalidSettings.Validate();
        Assert.False(invalidResult.Successful);
        Assert.Contains("Invalid repo mapping format", invalidResult.Message);

        var validSettings = new VaultImportSettings
        {
            ProjectName = "MyProject",
            Repos = ["repo1=C:/git/repo1", "repo2=C:/git/repo2"]
        };
        var validResult = validSettings.Validate();
        Assert.True(validResult.Successful);
    }

    [Fact]
    public void VaultImportSettings_RejectsEmptyProjectName()
    {
        var settings = new VaultImportSettings { ProjectName = "" };
        var result = settings.Validate();
        Assert.False(result.Successful);
    }

    [Fact]
    public void VaultPushSettings_RejectsEmptyProjectList()
    {
        var settings = new VaultPushSettings { Projects = [] };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("At least one project name must be specified", result.Message);
    }

    [Fact]
    public void VaultPushSettings_ValidProjects_Succeeds()
    {
        var settings = new VaultPushSettings { Projects = ["ProjectA", "ProjectB"] };
        var result = settings.Validate();
        Assert.True(result.Successful);
    }
}

public class VaultCommandsExecutionTests
{
    private static (CommandApp App, FakeVaultService VaultService, TestConsole Console) CreateTestApp()
    {
        var console = new TestConsole();
        var fakeVault = new FakeVaultService();
        var services = new ServiceCollection();
        services.AddSingleton<IVaultService>(fakeVault);
        var configService = new ConfigService(NullLogger<ConfigService>.Instance);
        services.AddSingleton<IConfigService>(configService);
        services.AddSingleton<ConfigService>(configService);

        var app = Program.ConfigureCliCommands(services, console);
        return (app, fakeVault, console);
    }

    [Fact]
    public void VaultListCommand_WithoutJson_RendersTable()
    {
        var (app, fakeVault, console) = CreateTestApp();
        fakeVault.VaultsToReturn =
        [
            new VaultStatus
            {
                Id = "vault-alpha",
                Name = "Alpha Vault",
                RepoUrl = "https://github.com/team/alpha-vault.git",
                CurrentBranch = "main",
                CommitsAhead = 0,
                CommitsBehind = 3,
                AlwaysUpToDate = true
            }
        ];

        var exit = app.Run(["vault", "list"]);

        Assert.Equal(0, exit);
        var output = console.Output;
        Assert.Contains("Alpha Vault", output);
        Assert.Contains("vault-alpha", output);
    }

    [Fact]
    public void VaultListCommand_WithJson_OutputsJson()
    {
        var (app, fakeVault, _) = CreateTestApp();
        fakeVault.VaultsToReturn =
        [
            new VaultStatus
            {
                Id = "vault-beta",
                Name = "Beta Vault",
                RepoUrl = "https://github.com/team/beta-vault.git",
                CurrentBranch = "main"
            }
        ];

        using var sw = new StringWriter();
        var prevOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var exit = app.Run(["vault", "list", "--json"]);
            Assert.Equal(0, exit);
            var output = sw.ToString();
            Assert.Contains("vault-beta", output);
            Assert.Contains("Beta Vault", output);
        }
        finally
        {
            Console.SetOut(prevOut);
        }
    }

    [Fact]
    public void VaultStatusCommand_ValidStatusResponse_RendersDetails()
    {
        var (app, fakeVault, console) = CreateTestApp();
        fakeVault.StatusToReturn = new VaultStatus
        {
            Id = "vault-prod",
            Name = "Production Vault",
            IsConfigured = true,
            RepoUrl = "https://github.com/team/prod-vault.git",
            LocalPath = "C:/vaults/prod",
            CurrentBranch = "main",
            LatestCommit = "deadbeef123",
            CommitsAhead = 2,
            CommitsBehind = 0,
            AlwaysUpToDate = true
        };

        var exit = app.Run(["vault", "status", "vault-prod"]);

        Assert.Equal(0, exit);
        var output = console.Output;
        Assert.Contains("Production Vault", output);
        Assert.Contains("vault-prod", output);
        Assert.Contains("deadbeef123", output);
    }

    [Fact]
    public void VaultDiscoverCommand_RendersDiscoveredRepositories()
    {
        var (app, fakeVault, console) = CreateTestApp();
        fakeVault.DiscoveredVaultsToReturn =
        [
            new DiscoveredVaultRepo("team/team-vault", "https://github.com/team/team-vault", "team", "team-vault", IsPrivate: true, "Organization"),
            new DiscoveredVaultRepo("pavel/personal-vault", "https://github.com/pavel/personal-vault", "pavel", "personal-vault", IsPrivate: false, "User")
        ];

        var exit = app.Run(["vault", "discover"]);

        Assert.Equal(0, exit);
        var output = console.Output;
        Assert.Contains("team/team-vault", output);
        Assert.Contains("pavel/personal-vault", output);
        Assert.Contains("Private", output);
        Assert.Contains("Public", output);
    }

    [Fact]
    public void VaultConnectCommand_ServiceReturnsFalse_ReturnsExitCodeOne()
    {
        var (app, fakeVault, console) = CreateTestApp();
        fakeVault.ConnectResultToReturn = new VaultResult(false, "Connection failed", "Authentication required");

        var exit = app.Run(["vault", "connect", "https://github.com/org/vault.git"]);

        Assert.Equal(1, exit);
        var output = console.Output;
        Assert.Contains("Error:", output);
        Assert.Contains("Authentication required", output);
    }

    [Fact]
    public void VaultCreateCommand_ServiceReturnsFalse_ReturnsExitCodeOne()
    {
        var (app, fakeVault, console) = CreateTestApp();
        fakeVault.CreateResultToReturn = new VaultResult(false, "Failed to create repository", "Repository name already exists");

        var exit = app.Run(["vault", "create", "existing-repo"]);

        Assert.Equal(1, exit);
        var output = console.Output;
        Assert.Contains("Error:", output);
        Assert.Contains("Repository name already exists", output);
    }
}
