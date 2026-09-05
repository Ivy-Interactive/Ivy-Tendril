using System;
using System.Collections.Generic;
using System.Linq;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Settings.Dialogs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.IssueTrackers;

namespace Ivy.Tendril.Apps.Settings;

public class IssueTrackersSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var issueTrackerService = UseService<IIssueTrackerService>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();

        var (dialogTrigger, showDialog) = UseTrigger((IState<bool> isOpen, TrackerConnectionConfig? existing) =>
            new EditTrackerConnectionDialog(isOpen, existing, config, issueTrackerService, client, () => refreshToken.Refresh()));

        var testingStates = UseState<Dictionary<string, string>>(() => new());

        var connections = config.Settings.TrackerConnections;

        async Task TestConnection(TrackerConnectionConfig conn)
        {
            var next = new Dictionary<string, string>(testingStates.Value) { [conn.Id] = "Testing..." };
            testingStates.Set(next);

            var provider = issueTrackerService.GetIssueProvider(conn.Provider);
            if (provider == null)
            {
                next = new Dictionary<string, string>(testingStates.Value) { [conn.Id] = "Provider not found" };
                testingStates.Set(next);
                return;
            }

            try
            {
                var result = await provider.GetMyAssignedIssuesAsync(conn);
                next = new Dictionary<string, string>(testingStates.Value)
                {
                    [conn.Id] = result.Error == null ? $"✓ Connected ({result.Value.Count} issues)" : $"✗ {result.Error}"
                };
                testingStates.Set(next);
            }
            catch (Exception ex)
            {
                next = new Dictionary<string, string>(testingStates.Value) { [conn.Id] = $"✗ {ex.Message}" };
                testingStates.Set(next);
            }
        }

        void DeleteConnection(string id)
        {
            var idx = connections.FindIndex(c => c.Id == id);
            if (idx >= 0)
            {
                var name = connections[idx].Name;
                connections.RemoveAt(idx);
                config.SaveSettings();
                refreshToken.Refresh();
                client.Toast($"Removed connection '{name}'", "Removed");
            }
        }

        var header = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
            | (Layout.Vertical().AlignContent(Align.Left)
                | Text.H2("Issue Trackers").Bold()
                | Text.Block("Connect multiple Jira workspaces, Linear teams, and GitHub accounts to aggregate issues and PRs across your projects.").Small().Muted())
            | new Button("Add Connection")
                .Icon(Icons.Plus)
                .Primary()
                .OnClick(() => showDialog(null));

        var githubDefaultCard = new Card(
            Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | Icons.Github.ToIcon()
                    | (Layout.Vertical().AlignContent(Align.Left)
                        | (Layout.Horizontal().AlignContent(Align.Left)
                            | Text.Block("GitHub (CLI / Default)").Bold()
                            | new Badge("Active").Variant(BadgeVariant.Secondary).Small())
                        | Text.Block("Uses your local GitHub CLI ('gh') authentication for repos and review requests.").Small().Muted()))
                | new Button("Add GitHub Token")
                    .Icon(Icons.Plus)
                    .Outline().Small()
                    .OnClick(() => showDialog(new TrackerConnectionConfig { Provider = "github", Name = "GitHub (Personal)" }))
        );

        var connectionCards = new List<object>();

        foreach (var conn in connections)
        {
            var providerIcon = conn.Provider switch
            {
                "jira" => Icons.SquareCheck,
                "linear" => Icons.Layers,
                "github" => Icons.Github,
                _ => Icons.SquareCheck
            };

            var providerBadgeVariant = conn.Provider switch
            {
                "jira" => BadgeVariant.Primary,
                "linear" => BadgeVariant.Outline,
                _ => BadgeVariant.Secondary
            };

            var detailsText = conn.Provider switch
            {
                "jira" => !string.IsNullOrEmpty(conn.Url) ? $"{conn.Url} ({conn.Email ?? "Token Auth"})" : "URL not set",
                "linear" => !string.IsNullOrEmpty(conn.ApiKey) ? "API Key configured" : "Key not set",
                "github" => !string.IsNullOrEmpty(conn.ApiToken) ? "Personal Access Token configured" : "Default CLI",
                _ => ""
            };

            testingStates.Value.TryGetValue(conn.Id, out var testResult);

            var currentConn = conn;
            var card = new Card(
                Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
                    | (Layout.Horizontal().AlignContent(Align.Left)
                        | providerIcon.ToIcon()
                        | (Layout.Vertical().AlignContent(Align.Left)
                            | (Layout.Horizontal().AlignContent(Align.Left)
                                | Text.Block(conn.Name).Bold()
                                | new Badge(conn.Provider.ToUpperInvariant()).Variant(providerBadgeVariant).Small())
                            | Text.Block(detailsText).Small().Muted()
                            | (testResult != null
                                ? Text.Block(testResult).Small().Color(testResult.StartsWith("✓") ? Colors.Green : Colors.Destructive)
                                : null)))
                    | (Layout.Horizontal().AlignContent(Align.Right)
                        | new Button("Test").Outline().Small().OnClick(async () => await TestConnection(currentConn))
                        | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit connection").OnClick(() => showDialog(currentConn))
                        | new Button().Icon(Icons.Trash2).Destructive().Small().Tooltip("Delete connection").OnClick(() => DeleteConnection(currentConn.Id)).WithConfirm(
                            $"Delete connection '{currentConn.Name}'?",
                            title: "Delete Connection",
                            confirmLabel: "Delete",
                            destructive: true))
            );

            connectionCards.Add(card);
        }

        var emptyView = connections.Count == 0
            ? new Card(
                Layout.Vertical().AlignContent(Align.Center).Width(Size.Full())
                    | Text.Block("No custom issue tracker connections configured yet.").Muted().Small()
                    | Text.Block("Click 'Add Connection' above to connect your Jira, Linear, or additional GitHub accounts.").Muted().Small()
              )
            : null;

        var content = Layout.Vertical().Width(Size.Full().Max(Size.Units(160)))
            | header
            | githubDefaultCard
            | connectionCards.ToArray()
            | emptyView
            | dialogTrigger;

        return Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full()).Height(Size.Full())
            | content;
    }
}
