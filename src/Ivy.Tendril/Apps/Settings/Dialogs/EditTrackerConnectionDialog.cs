using System;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.IssueTrackers;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class EditTrackerConnectionDialog(
    IState<bool> dialogOpen,
    TrackerConnectionConfig? existingConnection,
    IConfigService config,
    IIssueTrackerService issueTrackerService,
    IClientProvider client,
    Action onSaved) : ViewBase
{
    public override object? Build()
    {
        var name = UseState(() => existingConnection?.Name ?? "");
        var provider = UseState(() => existingConnection?.Provider ?? "jira");
        var url = UseState(() => existingConnection?.Url ?? "");
        var email = UseState(() => existingConnection?.Email ?? "");
        var apiToken = UseState(() => existingConnection?.ApiToken ?? "");
        var apiKey = UseState(() => existingConnection?.ApiKey ?? "");

        var isTesting = UseState(false);
        var testMessage = UseState<string?>(null);
        var isTestSuccess = UseState(false);
        var errorMessage = UseState<string?>(null);

        if (!dialogOpen.Value) return null;

        var isEditing = existingConnection != null;

        var providerOptions = new[]
        {
            new Option<string>("Jira", "jira"),
            new Option<string>("Linear", "linear"),
            new Option<string>("GitHub", "github")
        };

        async Task TestConnection()
        {
            isTesting.Set(true);
            testMessage.Set(null);
            errorMessage.Set(null);

            var tempConn = new TrackerConnectionConfig
            {
                Id = existingConnection?.Id ?? Guid.NewGuid().ToString("N"),
                Name = name.Value.Trim(),
                Provider = provider.Value,
                Url = url.Value.Trim(),
                Email = email.Value.Trim(),
                ApiToken = apiToken.Value.Trim(),
                ApiKey = apiKey.Value.Trim()
            };

            var trackerProvider = issueTrackerService.GetIssueProvider(provider.Value);
            if (trackerProvider == null)
            {
                isTesting.Set(false);
                isTestSuccess.Set(false);
                testMessage.Set($"Provider '{provider.Value}' is not supported.");
                return;
            }

            try
            {
                var result = await trackerProvider.GetMyAssignedIssuesAsync(tempConn);
                isTesting.Set(false);
                if (result.Error != null)
                {
                    isTestSuccess.Set(false);
                    testMessage.Set($"Connection failed: {result.Error}");
                }
                else
                {
                    isTestSuccess.Set(true);
                    testMessage.Set($"Connected successfully! Found {result.Value.Count} assigned issues.");
                }
            }
            catch (Exception ex)
            {
                isTesting.Set(false);
                isTestSuccess.Set(false);
                testMessage.Set($"Connection error: {ex.Message}");
            }
        }

        void Save()
        {
            var trimmedName = name.Value.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                errorMessage.Set("Connection name is required.");
                return;
            }

            if (provider.Value == "jira" && string.IsNullOrWhiteSpace(url.Value))
            {
                errorMessage.Set("Jira URL is required.");
                return;
            }

            if (provider.Value == "linear" && string.IsNullOrWhiteSpace(apiKey.Value))
            {
                errorMessage.Set("Linear API key is required.");
                return;
            }

            var connections = config.Settings.TrackerConnections;
            if (isEditing)
            {
                var idx = connections.FindIndex(c => c.Id == existingConnection!.Id);
                if (idx >= 0)
                {
                    connections[idx] = new TrackerConnectionConfig
                    {
                        Id = existingConnection!.Id,
                        Name = trimmedName,
                        Provider = provider.Value,
                        Url = url.Value.Trim(),
                        Email = email.Value.Trim(),
                        ApiToken = apiToken.Value.Trim(),
                        ApiKey = apiKey.Value.Trim()
                    };
                }
            }
            else
            {
                connections.Add(new TrackerConnectionConfig
                {
                    Name = trimmedName,
                    Provider = provider.Value,
                    Url = url.Value.Trim(),
                    Email = email.Value.Trim(),
                    ApiToken = apiToken.Value.Trim(),
                    ApiKey = apiKey.Value.Trim()
                });
            }

            try
            {
                config.SaveSettings();
                client.Toast(isEditing ? $"Updated {trimmedName}" : $"Added {trimmedName}", "Saved");
                dialogOpen.Set(false);
                onSaved();
            }
            catch (Exception ex)
            {
                errorMessage.Set($"Failed to save settings: {ex.Message}");
            }
        }

        var providerFields = provider.Value switch
        {
            "jira" => Layout.Vertical()
                | url.ToTextInput("https://your-company.atlassian.net")
                    .WithField().Label("Jira URL")
                | email.ToTextInput("name@company.com")
                    .WithField().Label("Account Email")
                | apiToken.ToPasswordInput("API token...")
                    .WithField().Label("API Token")
                | Text.Block("Generate an API token in your Atlassian Account Settings under Security > API tokens.").Small().Muted(),

            "linear" => Layout.Vertical()
                | apiKey.ToPasswordInput("lin_api_...")
                    .WithField().Label("Personal API Key")
                | Text.Block("Generate a Personal API Key in Linear under Settings > Account > API.").Small().Muted(),

            "github" => Layout.Vertical()
                | apiToken.ToPasswordInput("ghp_... (optional)")
                    .WithField().Label("Personal Access Token (optional)")
                | Text.Block("Leave blank to automatically use your local GitHub CLI ('gh') authentication.").Small().Muted(),

            _ => (object)Text.Block("Select a provider above.")
        };

        var form = Layout.Vertical()
            | name.ToTextInput("e.g. Work Jira, Personal Linear")
                .WithField().Label("Connection Name")
            | (!isEditing
                ? provider.ToSelectInput(providerOptions).WithField().Label("Issue Tracker Provider")
                : Layout.Horizontal().AlignContent(Align.Left)
                    | Text.Block("Provider:").Bold().Small()
                    | new Badge(provider.Value.ToUpperInvariant()).Variant(BadgeVariant.Outline).Small())
            | providerFields
            | (testMessage.Value != null
                ? Text.Block(testMessage.Value).Color(isTestSuccess.Value ? Colors.Green : Colors.Destructive).Small()
                : null)
            | (errorMessage.Value != null
                ? Text.Block(errorMessage.Value).Color(Colors.Destructive).Small()
                : null);

        var actions = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
            | new Button("Test Connection").Outline().Small()
                .Loading(isTesting.Value)
                .OnClick(async () => await TestConnection())
            | (Layout.Horizontal().AlignContent(Align.Right)
                | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
                | new Button(isEditing ? "Update Connection" : "Add Connection").Primary().OnClick(Save));

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader(isEditing ? $"Edit Tracker Connection: {existingConnection!.Name}" : "Add Issue Tracker Connection"),
            new DialogBody(form),
            new DialogFooter(actions)
        );
    }
}
