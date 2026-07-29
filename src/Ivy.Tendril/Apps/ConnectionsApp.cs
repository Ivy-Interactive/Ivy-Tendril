using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Connections;

namespace Ivy.Tendril.Apps;

[BetaApp]
[App(title: "Connections", icon: Icons.Plug, group: ["Automations"], order: 30)]
public class ConnectionsApp : ViewBase
{
    private record PresetIntegration(
        string Id,
        string Name,
        string Provider,
        string Subtitle,
        string Description,
        Icons Icon,
        string DefaultBotTokenPlaceholder,
        string DefaultAppTokenPlaceholder,
        string DefaultPermissions
    );

    private static readonly List<PresetIntegration> IntegrationsCatalog = new()
    {
        new PresetIntegration(
            "slack",
            "Slack Bot",
            "Slack",
            "Slack Bot",
            "Slack integration for automated messages, channel updates, and interactive notifications.",
            Icons.Slack,
            "xoxb-YOUR_SLACK_BOT_TOKEN",
            "xapp-YOUR_SLACK_APP_TOKEN",
            "send-message, add-reaction"
        ),
        new PresetIntegration(
            "github",
            "GitHub OAuth",
            "GitHub",
            "GitHub App",
            "GitHub integration for pull request management, issue tracking, and repository automation.",
            Icons.Github,
            "ghp_1234567890abcdefghijklmnopqrstuvwxyz",
            "github_pat_1234567890abcdefghijklmnopqrstuvwxyz",
            "create-pr, comment-pr, repo"
        ),
        new PresetIntegration(
            "discord",
            "Discord Webhook",
            "Discord",
            "Discord Bot",
            "Discord bot connection for real-time event notifications and interactive command handling.",
            Icons.Discord,
            "Bot token from Discord Developer Portal",
            "Client Secret / Public Key (optional)",
            "send-message"
        ),
        new PresetIntegration(
            "telegram",
            "Telegram Bot",
            "Telegram",
            "Telegram Bot",
            "Telegram bot API integration for direct messaging and channel broadcasts.",
            Icons.MessageSquare,
            "123456789:ABCdefGHIjklMNOpqrsTUVwxyZ",
            "",
            "send-message"
        ),
        new PresetIntegration(
            "jira",
            "Jira Service",
            "Jira",
            "Jira Cloud",
            "Jira Cloud API connection for issue sync and project board management.",
            Icons.Layers,
            "Jira API Token",
            "Jira User Email",
            "read-issues, write-issues"
        )
    };

    public override object Build()
    {
        var db = UseService<IPlanDatabaseService>();
        var executor = UseService<IConnectionExecutorService>();
        var client = UseService<IClientProvider>();

        var connections = UseState(db.GetConnections());
        var selectedId = UseState("slack");
        var searchQuery = UseState("");

        var botTokenInput = UseState("");
        var appTokenInput = UseState("");
        var permissionsInput = UseState("*");
        var isSaving = UseState(false);
        var isTesting = UseState(false);
        var testResult = UseState<string?>(null);
        var formError = UseState<string?>(null);

        var customProviderName = UseState("");
        var isCustomMode = UseState(false);

        // Helper to load connection data into inputs when selection changes
        void LoadSelectedIntegration(string id)
        {
            selectedId.Value = id;
            testResult.Value = null;
            formError.Value = null;

            var existing = connections.Value.FirstOrDefault(c =>
                c.Name.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                c.Provider.Equals(id, StringComparison.OrdinalIgnoreCase)
            );

            var preset = IntegrationsCatalog.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                isCustomMode.Value = false;
                permissionsInput.Value = existing.Permissions ?? "*";
                
                // Parse ConnectionString JSON if available
                if (!string.IsNullOrWhiteSpace(existing.ConnectionString) && existing.ConnectionString.TrimStart().StartsWith('{'))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(existing.ConnectionString);
                        botTokenInput.Value = doc.RootElement.TryGetProperty("Token", out var t) ? t.GetString() ?? "" : "";
                        appTokenInput.Value = doc.RootElement.TryGetProperty("AppToken", out var a) ? a.GetString() ?? "" : "";
                    }
                    catch
                    {
                        botTokenInput.Value = existing.ConnectionString;
                        appTokenInput.Value = "";
                    }
                }
                else
                {
                    botTokenInput.Value = existing.ConnectionString ?? "";
                    appTokenInput.Value = "";
                }
            }
            else if (preset != null)
            {
                isCustomMode.Value = false;
                botTokenInput.Value = "";
                appTokenInput.Value = "";
                permissionsInput.Value = preset.DefaultPermissions;
            }
            else
            {
                isCustomMode.Value = true;
                botTokenInput.Value = "";
                appTokenInput.Value = "";
                permissionsInput.Value = "*";
                customProviderName.Value = "";
            }
        }

        async Task SaveConnection()
        {
            var preset = IntegrationsCatalog.FirstOrDefault(i => i.Id.Equals(selectedId.Value, StringComparison.OrdinalIgnoreCase));
            var providerName = isCustomMode.Value ? (customProviderName.Value ?? "").Trim() : (preset?.Provider ?? selectedId.Value);
            var connName = isCustomMode.Value ? providerName : (preset?.Name ?? selectedId.Value);

            if (string.IsNullOrWhiteSpace(connName))
            {
                formError.Value = "Integration name is required.";
                return;
            }
            if (string.IsNullOrWhiteSpace(botTokenInput.Value))
            {
                formError.Value = "Bot Token is required.";
                return;
            }

            isSaving.Value = true;
            formError.Value = null;

            try
            {
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Token = botTokenInput.Value.Trim(),
                    AppToken = appTokenInput.Value?.Trim() ?? ""
                });

                var existing = db.GetConnectionByName(connName);
                var connItem = new ConnectionItem
                {
                    Name = connName,
                    Provider = providerName,
                    ConnectionString = payload,
                    Permissions = string.IsNullOrWhiteSpace(permissionsInput.Value) ? "*" : permissionsInput.Value.Trim(),
                    Created = existing?.Created ?? DateTime.UtcNow,
                    Updated = DateTime.UtcNow
                };

                db.UpsertConnection(connItem);
                connections.Value = db.GetConnections();
                client.Toast($"{connName} configured successfully!", "Saved", variant: ToastVariant.Success);
            }
            catch (Exception ex)
            {
                formError.Value = ex.Message;
            }
            finally
            {
                isSaving.Value = false;
            }
        }

        async Task TestSelectedConnection()
        {
            var preset = IntegrationsCatalog.FirstOrDefault(i => i.Id.Equals(selectedId.Value, StringComparison.OrdinalIgnoreCase));
            var connName = preset?.Name ?? selectedId.Value;

            var conn = db.GetConnectionByName(connName);
            if (conn == null)
            {
                testResult.Value = "Please save the connection before testing.";
                return;
            }

            isTesting.Value = true;
            testResult.Value = null;

            try
            {
                var (success, errorMsg) = await executor.TestConnectionAsync(conn);
                if (success)
                {
                    testResult.Value = "Success: Connection verified!";
                    client.Toast("Connection tested successfully!", "Verified", variant: ToastVariant.Success);
                }
                else
                {
                    testResult.Value = $"Error: {errorMsg}";
                }
            }
            catch (Exception ex)
            {
                testResult.Value = $"Error: {ex.Message}";
            }
            finally
            {
                isTesting.Value = false;
            }
        }

        void DeleteSelectedConnection()
        {
            var preset = IntegrationsCatalog.FirstOrDefault(i => i.Id.Equals(selectedId.Value, StringComparison.OrdinalIgnoreCase));
            var connName = preset?.Name ?? selectedId.Value;

            db.DeleteConnection(connName);
            connections.Value = db.GetConnections();
            botTokenInput.Value = "";
            appTokenInput.Value = "";
            client.Toast($"{connName} deleted.", "Deleted", variant: ToastVariant.Default);
        }

        // --- LEFT COLUMN: INTEGRATIONS LIST & SEARCH ---
        var filteredCatalog = IntegrationsCatalog
            .Where(i => string.IsNullOrEmpty(searchQuery.Value)
                        || i.Name.Contains(searchQuery.Value, StringComparison.OrdinalIgnoreCase)
                        || i.Subtitle.Contains(searchQuery.Value, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var leftList = Layout.Vertical().Gap(2);

        foreach (var item in filteredCatalog)
        {
            var isConfigured = connections.Value.Any(c =>
                c.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase) ||
                c.Provider.Equals(item.Provider, StringComparison.OrdinalIgnoreCase));

            var isSelected = selectedId.Value.Equals(item.Id, StringComparison.OrdinalIgnoreCase);

            var statusBadge = isConfigured
                ? new Badge("Active").Variant(BadgeVariant.Success)
                : new Badge("Inactive").Variant(BadgeVariant.Secondary);

            var card = new Box(
                Layout.Horizontal().AlignContent(Align.Left)
                   | item.Icon.ToIcon().Color(isSelected ? Colors.Primary : Colors.Muted)
                   | (Layout.Vertical().Width(Size.Grow())
                      | Text.H4(item.Name).Bold()
                      | Text.Muted(item.Subtitle).Small()
                     )
                   | statusBadge
            );
            card = card.Padding(3).OnClick(() => LoadSelectedIntegration(item.Id));

            leftList = leftList | card;
        }

        var addCustomCard = new Box(
            Layout.Horizontal().AlignContent(Align.Left)
               | Icons.Plus.ToIcon().Color(Colors.Primary)
               | (Layout.Vertical()
                  | Text.H4("Add Custom Integration").Bold()
                  | Text.Muted("Configure custom webhooks or secrets").Small()
                 )
        )
        .Padding(3)
        .OnClick(() => LoadSelectedIntegration("custom"));

        leftList = leftList | new Separator() | addCustomCard;

        var leftPanel = Layout.Vertical().Gap(3).Width(Size.Rem(22))
            | searchQuery.ToTextInput("Search integrations...")
                .Prefix(Icons.Search.ToIcon().Color(Colors.Muted))
            | leftList;

        // --- RIGHT COLUMN: SELECTED INTEGRATION DETAILS & CONFIGURATION ---
        var activePreset = IntegrationsCatalog.FirstOrDefault(i => i.Id.Equals(selectedId.Value, StringComparison.OrdinalIgnoreCase));

        var currentTitle = isCustomMode.Value ? "Custom Integration" : (activePreset?.Name ?? "Slack Bot");
        var currentDescription = isCustomMode.Value
            ? "Configure a custom integration token and permission parameters for arbitrary endpoints."
            : (activePreset?.Description ?? "Slack integration for automated messages, channel updates, and interactive notifications.");
        var currentProvider = isCustomMode.Value ? "Custom" : (activePreset?.Provider ?? "Slack");

        var isCurrentlyConfigured = connections.Value.Any(c =>
            c.Name.Equals(currentTitle, StringComparison.OrdinalIgnoreCase) ||
            c.Provider.Equals(currentProvider, StringComparison.OrdinalIgnoreCase));

        var headerActionRow = Layout.Horizontal().AlignContent(Align.Left).Gap(2)
            | new Button($"Create {currentTitle}")
                .Primary()
                .OnClick(() => _ = SaveConnection())
            | (isCurrentlyConfigured ? new Button("Test Connection").Variant(ButtonVariant.Outline).Loading(isTesting.Value).OnClick(() => _ = TestSelectedConnection()) : null)
            | (isCurrentlyConfigured ? new Button("Delete").Variant(ButtonVariant.Destructive).OnClick(() => DeleteSelectedConnection()) : null);

        var botTokenLabel = activePreset?.Provider switch
        {
            "Slack" => "Bot User OAuth Token",
            "GitHub" => "Personal Access Token (PAT)",
            "Discord" => "Bot Token",
            "Telegram" => "Bot API Token",
            "Jira" => "API Token",
            _ => "Bot / Secret Token"
        };

        var botTokenPlaceholder = activePreset?.DefaultBotTokenPlaceholder ?? "starts with xoxb-";
        var appTokenPlaceholder = activePreset?.DefaultAppTokenPlaceholder ?? "starts with xapp-";

        var configForm = Layout.Vertical().Gap(4)
            | (isCustomMode.Value
                ? customProviderName.ToTextInput("e.g. Linear")
                    .WithField()
                    .Label("Provider / Integration Name")
                    .Required()
                : null)
            | botTokenInput.ToTextInput(botTokenPlaceholder)
                .WithField()
                .Label(botTokenLabel)
                .Description($"OAuth token value for {currentTitle} ({botTokenPlaceholder})")
                .Required()
            | (activePreset?.Provider == "Slack" || activePreset?.Provider == "GitHub" || isCustomMode.Value
                ? appTokenInput.ToTextInput(appTokenPlaceholder)
                    .WithField()
                    .Label("App-Level Token")
                    .Description("App-Level Token for Socket Mode or Webhook verification (xapp-...)")
                : null)
            | permissionsInput.ToTextInput("send-message, add-reaction")
                .WithField()
                .Label("Permissions / Scopes")
                .Description("Allowed actions for this connection (comma-separated or * for all).")
            | (formError.Value != null ? Text.Danger(formError.Value) : null)
            | (testResult.Value != null ? (testResult.Value.StartsWith("Success") ? Text.Success(testResult.Value) : Text.Danger(testResult.Value)) : null)
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Button("Save Configuration")
                    .Primary()
                    .Loading(isSaving.Value)
                    .OnClick(() => _ = SaveConnection())
              );

        var rightPanel = Layout.Vertical().Gap(4).Width(Size.Grow())
            | (Layout.Vertical().Gap(2)
               | (Layout.Horizontal().AlignContent(Align.Left)
                  | (Layout.Vertical().Width(Size.Grow())
                     | Text.H2(currentTitle).Bold()
                     | Text.Muted(currentDescription)
                    )
                 )
               | headerActionRow
              )
            | new Separator()
            | new Box(
                Layout.Vertical().Gap(3)
                   | Text.H3("Configuration").Bold()
                   | configForm
              ).Padding(4);

        return Layout.Vertical().Height(Size.Full()).RemoveParentPadding()
            | (Layout.Horizontal().AlignContent(Align.Left).Gap(6).Padding(4)
               | leftPanel
               | rightPanel
              );
    }
}
