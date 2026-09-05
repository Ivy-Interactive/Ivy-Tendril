using System;
using System.Collections.Generic;
using System.Linq;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class EditProjectTrackerDialog(
    IState<bool> dialogOpen,
    ProjectTrackerConfig? existingTracker,
    IConfigService config,
    ProjectConfig project,
    Action<ProjectTrackerConfig> onSaved) : ViewBase
{
    public override object? Build()
    {
        var trackerName = UseState(() => existingTracker?.Name ?? "");
        var selectedSource = UseState(() =>
        {
            if (existingTracker != null)
            {
                if (!string.IsNullOrEmpty(existingTracker.ConnectionId))
                    return $"conn:{existingTracker.ConnectionId}";
                return existingTracker.Provider ?? "github";
            }
            return "github";
        });

        var projectKey = UseState(() => existingTracker?.ProjectKey ?? "");
        var teamKey = UseState(() => existingTracker?.TeamKey ?? "");
        var repo = UseState(() => existingTracker?.Repo ?? "");
        var errorMessage = UseState<string?>(null);

        if (!dialogOpen.Value) return null;

        var isEditing = existingTracker != null;
        var connections = config.Settings.TrackerConnections;

        var sourceOptions = new List<Option<string>>
        {
            new("GitHub (Default / gh CLI)", "github")
        };

        foreach (var conn in connections)
        {
            sourceOptions.Add(new Option<string>($"{conn.Name} ({conn.Provider.ToUpperInvariant()})", $"conn:{conn.Id}"));
        }

        if (!connections.Any(c => c.Provider == "jira"))
        {
            sourceOptions.Add(new Option<string>("Jira (Global / Env Config)", "jira"));
        }
        if (!connections.Any(c => c.Provider == "linear"))
        {
            sourceOptions.Add(new Option<string>("Linear (Global / Env Config)", "linear"));
        }

        string effectiveProvider;
        string? effectiveConnectionId = null;

        if (selectedSource.Value.StartsWith("conn:"))
        {
            effectiveConnectionId = selectedSource.Value["conn:".Length..];
            var matched = connections.FirstOrDefault(c => c.Id == effectiveConnectionId);
            effectiveProvider = matched?.Provider ?? "github";
        }
        else
        {
            effectiveProvider = selectedSource.Value;
        }

        void Save()
        {
            if (effectiveProvider == "jira" && string.IsNullOrWhiteSpace(projectKey.Value))
            {
                errorMessage.Set("Jira Project Key is required (e.g. ENG, PROJ).");
                return;
            }

            if (effectiveProvider == "linear" && string.IsNullOrWhiteSpace(teamKey.Value))
            {
                errorMessage.Set("Linear Team Key is required (e.g. ENG, CORE).");
                return;
            }

            var connObj = effectiveConnectionId != null
                ? connections.FirstOrDefault(c => c.Id == effectiveConnectionId)
                : null;

            var defaultLabel = effectiveProvider switch
            {
                "jira" => $"{projectKey.Value.Trim()} (Jira)",
                "linear" => $"{teamKey.Value.Trim()} (Linear)",
                "github" => !string.IsNullOrWhiteSpace(repo.Value) ? repo.Value.Trim() : "GitHub Repositories",
                _ => effectiveProvider
            };

            var finalName = !string.IsNullOrWhiteSpace(trackerName.Value)
                ? trackerName.Value.Trim()
                : (connObj != null ? $"{connObj.Name} ({defaultLabel})" : defaultLabel);

            var savedTracker = new ProjectTrackerConfig
            {
                Id = existingTracker?.Id ?? Guid.NewGuid().ToString("N"),
                Name = finalName,
                Provider = effectiveProvider,
                ConnectionId = effectiveConnectionId,
                ProjectKey = effectiveProvider == "jira" ? projectKey.Value.Trim() : null,
                TeamKey = effectiveProvider == "linear" ? teamKey.Value.Trim() : null,
                Repo = effectiveProvider == "github" ? repo.Value.Trim() : null
            };

            dialogOpen.Set(false);
            onSaved(savedTracker);
        }

        var targetFields = effectiveProvider switch
        {
            "jira" => Layout.Vertical()
                | projectKey.ToTextInput("e.g. ENG, PROJ")
                    .WithField().Label("Jira Project Key")
                | Text.Block("Issues from this Jira project will be tracked in Tendril.").Small().Muted(),

            "linear" => Layout.Vertical()
                | teamKey.ToTextInput("e.g. ENG, CORE")
                    .WithField().Label("Linear Team Key")
                | Text.Block("Issues from this Linear team will be tracked in Tendril.").Small().Muted(),

            "github" => Layout.Vertical()
                | repo.ToTextInput("e.g. owner/repo (optional)")
                    .WithField().Label("GitHub Repository Override (Optional)")
                | Text.Block("Leave blank to automatically track issues from this project's configured git repositories.").Small().Muted(),

            _ => (object)Text.Block("Select an issue tracker above.")
        };

        var form = Layout.Vertical()
            | selectedSource.ToSelectInput(sourceOptions.ToArray()).WithField().Label("Issue Tracker Source")
            | targetFields
            | trackerName.ToTextInput("e.g. Public GitHub, Internal Jira (optional)")
                .WithField().Label("Display Label (Optional)")
            | (errorMessage.Value != null
                ? Text.Block(errorMessage.Value).Color(Colors.Destructive).Small()
                : null);

        var actions = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
            | new Button(isEditing ? "Save Tracker" : "Link Tracker").Primary().OnClick(Save);

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader(isEditing ? "Edit Linked Issue Tracker" : "Link Issue Tracker to Project"),
            new DialogBody(form),
            new DialogFooter(actions)
        );
    }
}
