using System.Text.RegularExpressions;
using Ivy.Tendril.Plugin.Linear.GraphQL;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Plugin.Linear;

internal class ImportFromLinearDialog(IState<bool> dialogOpen, LinearClientFactory clientFactory, string tendrilHome) : ViewBase
{
    public override object? Build()
    {
        var client = UseService<IClientProvider>();
        var logger = UseService<ILogger<ImportFromLinearDialog>>();

        var selectedTeam = UseState<string?>(null);
        var selectedProject = UseState<string?>(null);
        var selectedAssignee = UseState<string?>(null);
        var selectedLabel = UseState<string?>(null);
        var selectedPriority = UseState<string?>(null);
        var selectedStatus = UseState<string?>(null);
        var searchText = UseState("");
        var issues = UseState<IReadOnlyList<LinearIssueInfo>?>(null);
        var selectedIssueIds = UseState<HashSet<string>>([]);
        var isFetching = UseState(false);
        var isImporting = UseState(false);
        var error = UseState<string?>(null);

        var teamsQuery = UseQuery<IReadOnlyList<LinearTeamInfo>, string?>(
            dialogOpen.Value ? "linear:teams" : null,
            async (_, ct) =>
            {
                var result = await clientFactory.Client.GetTeams.ExecuteAsync(ct);
                if (result.Errors is { Count: > 0 } errors)
                    throw new Exception(errors[0].Message);
                return result.Data!.Teams.Nodes
                    .Select(t => new LinearTeamInfo(t.Id, t.Name, t.Key))
                    .ToList();
            },
            initialValue: []);

        var projectsQuery = UseQuery<IReadOnlyList<LinearProjectInfo>, string?>(
            dialogOpen.Value ? "linear:projects" : null,
            async (_, ct) =>
            {
                var result = await clientFactory.Client.GetProjects.ExecuteAsync(ct);
                if (result.Errors is { Count: > 0 } errors)
                    throw new Exception(errors[0].Message);
                return result.Data!.Projects.Nodes
                    .Select(p => new LinearProjectInfo(p.Id, p.Name))
                    .ToList();
            },
            initialValue: []);

        var usersQuery = UseQuery<IReadOnlyList<LinearUserInfo>, string?>(
            dialogOpen.Value ? "linear:users" : null,
            async (_, ct) =>
            {
                var result = await clientFactory.Client.GetUsers.ExecuteAsync(ct);
                if (result.Errors is { Count: > 0 } errors)
                    throw new Exception(errors[0].Message);
                return result.Data!.Users.Nodes
                    .Select(u => new LinearUserInfo(u.Id, u.DisplayName))
                    .ToList();
            },
            initialValue: []);

        var labelsQuery = UseQuery<IReadOnlyList<LinearLabelInfo>, string?>(
            dialogOpen.Value ? "linear:labels" : null,
            async (_, ct) =>
            {
                var result = await clientFactory.Client.GetIssueLabels.ExecuteAsync(ct);
                if (result.Errors is { Count: > 0 } errors)
                    throw new Exception(errors[0].Message);
                return result.Data!.IssueLabels.Nodes
                    .Select(l => new LinearLabelInfo(l.Id, l.Name, l.Team?.Id))
                    .ToList();
            },
            initialValue: []);

        var statesQuery = UseQuery<IReadOnlyList<LinearStateInfo>, string?>(
            dialogOpen.Value ? "linear:states" : null,
            async (_, ct) =>
            {
                var result = await clientFactory.Client.GetWorkflowStates.ExecuteAsync(ct);
                if (result.Errors is { Count: > 0 } errors)
                    throw new Exception(errors[0].Message);
                return result.Data!.WorkflowStates.Nodes
                    .Select(s => new LinearStateInfo(s.Id, s.Name, s.Type, s.Team.Id))
                    .OrderBy(s => s.Type)
                    .ToList();
            },
            initialValue: []);

        UseEffect(() =>
        {
            if (!dialogOpen.Value)
            {
                selectedTeam.Set(null);
                selectedProject.Set(null);
                selectedAssignee.Set(null);
                selectedLabel.Set(null);
                selectedPriority.Set(null);
                selectedStatus.Set(null);
                searchText.Set("");
                issues.Set(null);
                selectedIssueIds.Set([]);
                error.Set(null);
            }
        }, dialogOpen);

        if (!dialogOpen.Value) return null;

        if (teamsQuery.Loading || projectsQuery.Loading || usersQuery.Loading || labelsQuery.Loading || statesQuery.Loading)
        {
            return new Dialog(
                _ => dialogOpen.Set(false),
                new DialogHeader("Import Issues from Linear"),
                new DialogBody(
                    Layout.Vertical().Gap(3).AlignContent(Align.Center)
                    | new Loading()
                    | Text.Muted("Loading filters...")
                )
            ).Width(Size.Rem(48));
        }

        if ((teamsQuery.Error ?? projectsQuery.Error ?? usersQuery.Error ?? labelsQuery.Error ?? statesQuery.Error) is { } err)
        {
            return new Dialog(
                _ => dialogOpen.Set(false),
                new DialogHeader("Import Issues from Linear"),
                new DialogBody(Text.Danger(err.Message))
            ).Width(Size.Rem(48));
        }

        var teamList = teamsQuery.Value ?? [];
        var teamOptions = teamList.Select(t => $"{t.Key} — {t.Name}").ToArray();
        var projectList = projectsQuery.Value ?? [];
        var projectOptions = projectList.Select(p => p.Name).ToArray();
        var userList = usersQuery.Value ?? [];
        var userOptions = userList.Select(u => u.DisplayName).ToArray();
        var priorityOptions = new[] { "Urgent", "High", "Medium", "Low", "No priority" };

        var selectedTeamId = selectedTeam.Value is not null
            ? teamList.FirstOrDefault(t => $"{t.Key} — {t.Name}" == selectedTeam.Value)?.Id
            : null;

        var allLabels = labelsQuery.Value ?? [];
        var labelList = selectedTeamId is not null
            ? allLabels.Where(l => l.TeamId is null || l.TeamId == selectedTeamId).ToList()
            : allLabels;
        var labelOptions = labelList.Select(l => l.Name).ToArray();

        var allStates = statesQuery.Value ?? [];
        var stateList = (selectedTeamId is not null
            ? allStates.Where(s => s.TeamId == selectedTeamId)
            : allStates)
            .DistinctBy(s => s.Name)
            .ToList();
        var stateOptions = stateList.Select(s => s.Name).ToArray();

        if (selectedLabel.Value is not null && !labelOptions.Contains(selectedLabel.Value))
            selectedLabel.Set(null);
        if (selectedStatus.Value is not null && !stateOptions.Contains(selectedStatus.Value))
            selectedStatus.Set(null);

        async Task FetchIssues()
        {
            var teamLabel = selectedTeam.Value;
            var projectLabel = selectedProject.Value;
            var assigneeLabel = selectedAssignee.Value;
            var labelLabel = selectedLabel.Value;
            var priorityLabel = selectedPriority.Value;
            var statusLabel = selectedStatus.Value;
            var search = searchText.Value.Trim();

            var hasAnyFilter = teamLabel is not null || projectLabel is not null ||
                               assigneeLabel is not null || labelLabel is not null ||
                               priorityLabel is not null || statusLabel is not null ||
                               !string.IsNullOrEmpty(search);
            if (!hasAnyFilter) return;

            var team = teamLabel is not null
                ? teamList.FirstOrDefault(t => $"{t.Key} — {t.Name}" == teamLabel)
                : null;
            var project = projectLabel is not null
                ? projectList.FirstOrDefault(p => p.Name == projectLabel)
                : null;
            var assignee = assigneeLabel is not null
                ? userList.FirstOrDefault(u => u.DisplayName == assigneeLabel)
                : null;
            var label = labelLabel is not null
                ? labelList.FirstOrDefault(l => l.Name == labelLabel)
                : null;
            var state = statusLabel is not null
                ? stateList.FirstOrDefault(s => s.Name == statusLabel)
                : null;

            if (teamLabel is not null && team is null) return;
            if (projectLabel is not null && project is null) return;
            if (assigneeLabel is not null && assignee is null) return;
            if (labelLabel is not null && label is null) return;
            if (statusLabel is not null && state is null) return;

            isFetching.Set(true);
            error.Set(null);
            issues.Set(null);
            selectedIssueIds.Set([]);
            try
            {
                var filter = new GraphQL.IssueFilter();
                if (team is not null)
                    filter.Team = new GraphQL.TeamFilter { Id = new GraphQL.IDComparator { Eq = team.Id } };
                if (project is not null)
                    filter.Project = new GraphQL.NullableProjectFilter { Id = new GraphQL.IDComparator { Eq = project.Id } };
                if (assignee is not null)
                    filter.Assignee = new GraphQL.NullableUserFilter { Id = new GraphQL.IDComparator { Eq = assignee.Id } };
                if (label is not null)
                    filter.Labels = new GraphQL.IssueLabelCollectionFilter { Id = new GraphQL.IDComparator { Eq = label.Id } };
                if (priorityLabel is not null)
                {
                    var priorityValue = priorityLabel switch
                    {
                        "Urgent" => 1,
                        "High" => 2,
                        "Medium" => 3,
                        "Low" => 4,
                        _ => 0
                    };
                    filter.Priority = new GraphQL.NullableNumberComparator { Eq = priorityValue };
                }
                if (state is not null)
                    filter.State = new GraphQL.WorkflowStateFilter { Name = new GraphQL.StringComparator { Eq = state.Name } };
                if (!string.IsNullOrEmpty(search))
                    filter.Title = new GraphQL.StringComparator { ContainsIgnoreCase = search };

                var result = await clientFactory.Client.GetIssues.ExecuteAsync(50, null, filter);
                if (result.Errors is { Count: > 0 } errors)
                    throw new Exception(errors[0].Message);
                var fetched = result.Data!.Issues.Nodes
                    .Select(i => new LinearIssueInfo(
                        Id: i.Id,
                        Identifier: i.Identifier,
                        Title: i.Title,
                        Description: i.Description,
                        Priority: (int)i.Priority,
                        PriorityLabel: i.PriorityLabel,
                        Url: i.Url,
                        StateName: i.State.Name,
                        StateType: i.State.Type,
                        AssigneeName: i.Assignee?.Name,
                        Labels: i.Labels.Nodes.Select(l => l.Name).ToList()))
                    .ToList();
                issues.Set(fetched);
                selectedIssueIds.Set(fetched.Select(i => i.Id).ToHashSet());
            }
            catch (Exception ex)
            {
                error.Set($"Failed to fetch issues: {ex.Message}");
                logger.LogWarning(ex, "Failed to fetch Linear issues");
            }
            finally
            {
                isFetching.Set(false);
            }
        }

        bool HasAnyFilter() =>
            selectedTeam.Value is not null || selectedProject.Value is not null ||
            selectedAssignee.Value is not null || selectedLabel.Value is not null ||
            selectedPriority.Value is not null || selectedStatus.Value is not null ||
            !string.IsNullOrWhiteSpace(searchText.Value);

        async Task ImportSelected()
        {
            if (issues.Value is not { Count: > 0 } allIssues) return;
            if (selectedIssueIds.Value.Count == 0) return;

            isImporting.Set(true);
            try
            {
                var inboxPath = Path.Combine(tendrilHome, "Inbox");
                Directory.CreateDirectory(inboxPath);

                var importedCount = 0;
                foreach (var issue in allIssues.Where(i => selectedIssueIds.Value.Contains(i.Id)))
                {
                    var safeName = SanitizeFileName(issue.Title);
                    var fileName = $"{issue.Identifier}-{safeName}.md";
                    var filePath = Path.Combine(inboxPath, fileName);

                    if (File.Exists(filePath)) continue;

                    var labels = issue.Labels.Count > 0
                        ? $"\nlabels: [{string.Join(", ", issue.Labels)}]"
                        : "";

                    var content = $"""
                                   ---
                                   project: Auto
                                   sourceUrl: {issue.Url}
                                   sourceIdentifier: {issue.Identifier}{labels}
                                   ---
                                   [{issue.Identifier}]({issue.Url})

                                   {issue.Description ?? "No description."}
                                   """;

                    await File.WriteAllTextAsync(filePath, content);
                    importedCount++;
                }

                client.Toast($"Imported {importedCount} issue{(importedCount == 1 ? "" : "s")} to Inbox", "Import Complete");
                dialogOpen.Set(false);
            }
            catch (Exception ex)
            {
                error.Set($"Import failed: {ex.Message}");
                logger.LogWarning(ex, "Linear import failed");
            }
            finally
            {
                isImporting.Set(false);
            }
        }

        void ToggleIssue(string id)
        {
            var next = new HashSet<string>(selectedIssueIds.Value);
            if (!next.Remove(id))
                next.Add(id);
            selectedIssueIds.Set(next);
        }

        object? issuesPanel;
        if (isFetching.Value)
        {
            issuesPanel = Layout.Vertical().Gap(2).AlignContent(Align.Center)
                .Height(Size.Rem(16)).Width(Size.Full())
                | new Loading()
                | Text.Muted("Fetching issues from Linear...");
        }
        else if (issues.Value is { } issueList)
        {
            if (issueList.Count == 0)
            {
                issuesPanel = Layout.Vertical().AlignContent(Align.Center)
                    .Height(Size.Rem(10)).Width(Size.Full())
                    | Text.Muted("No issues found for the selected filters.");
            }
            else
            {
                var rows = issueList.Select(issue =>
                {
                    var isSelected = selectedIssueIds.Value.Contains(issue.Id);
                    return (object)(Layout.Horizontal().Gap(2).AlignContent(Align.Center).Width(Size.Full())
                        | new Button()
                            .Icon(isSelected ? Icons.Check : Icons.Square)
                            .Ghost().Small()
                            .Width(Size.Shrink())
                            .OnClick(() => ToggleIssue(issue.Id))
                        | (Layout.Vertical().Width(Size.Grow().Min(Size.Px(0)))
                            | Text.Block($"{issue.Identifier} — {issue.Title}")
                            | (Layout.Horizontal().Gap(2)
                                | Text.Muted(issue.StateName).Small()
                                | Text.Muted(issue.PriorityLabel).Small()
                                | (issue.AssigneeName is { } a ? Text.Muted(a).Small() : null)))
                        | new Button().Icon(Icons.ExternalLink).Ghost().Small()
                            .Width(Size.Shrink())
                            .Tooltip("Open in Linear")
                            .OnClick(() => client.OpenUrl(issue.Url)));
                }).ToArray();

                var selectedCount = selectedIssueIds.Value.Count;
                issuesPanel = Layout.Vertical().Gap(2).Width(Size.Full())
                    | (Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween).Width(Size.Full())
                        | Text.Label($"{issueList.Count} issues · {selectedCount} selected")
                        | (Layout.Horizontal().Gap(1)
                            | new Button("All").Ghost().Small()
                                .Disabled(selectedCount == issueList.Count)
                                .OnClick(() => selectedIssueIds.Set(issueList.Select(i => i.Id).ToHashSet()))
                            | new Button("None").Ghost().Small()
                                .Disabled(selectedCount == 0)
                                .OnClick(() => selectedIssueIds.Set([]))))
                    | (Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Rem(20)).Width(Size.Full())
                        | new List(rows).Width(Size.Full()));
            }
        }
        else
        {
            issuesPanel = Layout.Vertical().AlignContent(Align.Center)
                .Height(Size.Rem(10)).Width(Size.Full())
                | Text.Muted("Choose filters and click Fetch Issues.");
        }

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Import Issues from Linear"),
            new DialogBody(
                Layout.Vertical().Gap(3)
                | (Layout.Horizontal().Gap(3)
                    | selectedTeam.ToSelectInput(teamOptions.ToOptions()).Nullable()
                        .WithField().Label("Team")
                    | selectedProject.ToSelectInput(projectOptions.ToOptions()).Nullable()
                        .WithField().Label("Project"))
                | (Layout.Horizontal().Gap(3)
                    | selectedAssignee.ToSelectInput(userOptions.ToOptions()).Nullable()
                        .WithField().Label("Assignee")
                    | selectedLabel.ToSelectInput(labelOptions.ToOptions()).Nullable()
                        .WithField().Label("Label"))
                | (Layout.Horizontal().Gap(3)
                    | selectedPriority.ToSelectInput(priorityOptions.ToOptions()).Nullable()
                        .WithField().Label("Priority")
                    | selectedStatus.ToSelectInput(stateOptions.ToOptions()).Nullable()
                        .WithField().Label("Status"))
                | searchText.ToTextInput().Placeholder("Search by title...")
                    .WithField().Label("Search")
                | new Button("Fetch Issues").Outline().Loading(isFetching.Value)
                    .Disabled(!HasAnyFilter() || isFetching.Value)
                    .OnClick(async () => await FetchIssues())
                | (error.Value is { } e ? Text.Danger(e).Small() : null)
                | issuesPanel
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button(selectedIssueIds.Value.Count > 0
                        ? $"Import Selected ({selectedIssueIds.Value.Count})"
                        : "Import Selected")
                    .Primary()
                    .Loading(isImporting.Value)
                    .Disabled(selectedIssueIds.Value.Count == 0 || issues.Value is null)
                    .OnClick(async () => await ImportSelected())
            )
        ).Width(Size.Rem(48));
    }

    private static string SanitizeFileName(string title)
    {
        var sanitized = Regex.Replace(title, @"[^a-zA-Z0-9\s-]", "");
        sanitized = Regex.Replace(sanitized, @"\s+", "-");
        sanitized = sanitized.Trim('-').ToLowerInvariant();
        return sanitized.Length > 60 ? sanitized[..60].TrimEnd('-') : sanitized;
    }
}

internal record LinearTeamInfo(string Id, string Name, string Key);

internal record LinearProjectInfo(string Id, string Name);

internal record LinearUserInfo(string Id, string DisplayName);

internal record LinearLabelInfo(string Id, string Name, string? TeamId);

internal record LinearStateInfo(string Id, string Name, string Type, string TeamId);

internal record LinearIssueInfo(
    string Id,
    string Identifier,
    string Title,
    string? Description,
    int Priority,
    string PriorityLabel,
    string Url,
    string StateName,
    string StateType,
    string? AssigneeName,
    IReadOnlyList<string> Labels);
