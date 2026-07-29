using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services.Connections;

namespace Ivy.Tendril.Apps;

[BetaApp]
[App(title: "Connections", icon: Icons.Plug, group: ["Automations"], order: 30)]
public class ConnectionsApp : ViewBase
{
    private const string TagMyConnections = "my-connections";
    private const string TagCatalog = "catalog";

    public override object Build()
    {
        var db = UseService<IPlanDatabaseService>();
        var executor = UseService<IConnectionExecutorService>();
        var navigator = UseNavigation();
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var providers = UseService<IEnumerable<IConnectionProvider>>();

        var connections = UseState(db.GetConnections());
        var testStatuses = UseState(new Dictionary<string, string>());

        var selectedSection = UseState(TagMyConnections);
        var activeStep = UseState("catalog"); // "catalog" or "form"
        var formProvider = UseState("Slack");

        var newName = UseState("");
        var newConfig = UseState("");
        var newPermissions = UseState("*");
        var customProviderType = UseState("");
        var formError = UseState<string?>(null);
        var isSaving = UseState(false);

        var searchQuery = UseState("");
        var catalogSearchQuery = UseState("");

        var isRequestOpen = UseState(false);
        var requestDescription = UseState("");
        var requestGithubUser = UseState("");
        var isSubmittingRequest = UseState(false);
        var requestError = UseState<string?>(null);

        var editName = UseState("");
        var editConfig = UseState("");
        var editPermissions = UseState("");
        var editError = UseState<string?>(null);
        var isEditSaving = UseState(false);
        var editingConn = UseState<ConnectionItem?>(null);

        var (editSheetView, triggerEdit) = UseTrigger<ConnectionItem>((isOpen, conn) =>
        {
            if (conn == null) return new Fragment();

            var tokenLabel = conn.Provider switch
            {
                "Slack" => "OAuth Bot Token",
                "Discord" => "Bot Token",
                "GitHub" => "Personal Access Token (PAT)",
                _ => "API Token / Secret Key"
            };

            var tokenPlaceholder = conn.Provider switch
            {
                "Slack" => "starts with xoxb-",
                "Discord" => "Bot token from Discord Developer Portal",
                "GitHub" => "starts with ghp_ or github_pat_",
                _ => "token value"
            };

            var permissionsHelp = conn.Provider switch
            {
                "Slack" => "Allowed actions. E.g. send-message, add-reaction or * for all.",
                "Discord" => "Allowed actions. E.g. send-message or * for all.",
                "GitHub" => "Allowed actions. E.g. create-pr, comment-pr or * for all.",
                _ => "Allowed actions (comma-separated)."
            };

            var nameField = editName.ToTextInput("e.g. production-alerts")
                .WithField()
                .Label("Connection Name")
                .Required();

            var tokenField = editConfig.ToTextInput(tokenPlaceholder)
                .WithField()
                .Label(tokenLabel)
                .Required();

            var permissionsField = editPermissions.ToTextInput("e.g. *")
                .WithField()
                .Label("Permissions")
                .Description(permissionsHelp);

            var secretsInfo = conn.Provider switch
            {
                "Slack" => (object)(Layout.Vertical()
                    | (Layout.Horizontal().AlignContent(Align.Left)
                       | Text.Muted("Need a Slack Bot Token?").Small()
                       | new Button("Go to Slack App Console")
                           .Variant(ButtonVariant.Ghost)
                           .Small()
                           .OnClick(() => navigator.Navigate("https://api.slack.com/apps"))
                      )
                    | Text.Muted("Create an app in your Slack workspace, enable 'bots' features, install it, and copy the Bot User OAuth Token (xoxb-...).").Small()),
                "Discord" => (object)(Layout.Vertical()
                    | (Layout.Horizontal().AlignContent(Align.Left)
                       | Text.Muted("Need a Discord Bot Token?").Small()
                       | new Button("Go to Discord Developer Portal")
                           .Variant(ButtonVariant.Ghost)
                           .Small()
                           .OnClick(() => navigator.Navigate("https://discord.com/developers/applications"))
                      )
                    | Text.Muted("Create an application, add a Bot under the Bot tab, click 'Reset Token' to generate, and copy the Bot Token.").Small()),
                "GitHub" => (object)(Layout.Vertical()
                    | (Layout.Horizontal().AlignContent(Align.Left)
                       | Text.Muted("Need a GitHub Personal Access Token?").Small()
                       | new Button("Go to GitHub Tokens Settings")
                           .Variant(ButtonVariant.Ghost)
                           .Small()
                           .OnClick(() => navigator.Navigate("https://github.com/settings/tokens"))
                      )
                    | Text.Muted("Generate a Personal Access Token (Classic or Fine-grained) with 'repo' scope (and 'workflow' if editing workflows) and copy the token (ghp_...).").Small()),
                _ => new Fragment()
            };

            async Task SaveEditConnection()
            {
                var name = (editName.Value ?? "").Trim();
                var configText = (editConfig.Value ?? "").Trim();
                var perms = (editPermissions.Value ?? "").Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    editError.Value = "Name is required.";
                    return;
                }
                if (string.IsNullOrWhiteSpace(configText))
                {
                    editError.Value = "Token/Secret is required.";
                    return;
                }

                if (!string.Equals(conn.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (db.GetConnectionByName(name) != null)
                    {
                        editError.Value = $"A connection with name '{name}' already exists.";
                        return;
                    }
                }

                isEditSaving.Value = true;
                editError.Value = null;

                try
                {
                    var configJson = configText;
                    if (!configText.StartsWith('{'))
                    {
                        try
                        {
                            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
                            var dict = deserializer.Deserialize<Dictionary<string, object>>(configText);
                            if (dict != null && dict.Count > 0)
                            {
                                configJson = System.Text.Json.JsonSerializer.Serialize(dict);
                            }
                            else
                            {
                                configJson = System.Text.Json.JsonSerializer.Serialize(new { Token = configText });
                            }
                        }
                        catch
                        {
                            configJson = System.Text.Json.JsonSerializer.Serialize(new { Token = configText });
                        }
                    }

                    var connItem = new ConnectionItem
                    {
                        Name = name,
                        Provider = conn.Provider,
                        ConnectionString = configJson,
                        Permissions = string.IsNullOrWhiteSpace(perms) ? "*" : perms,
                        Created = conn.Created,
                        Updated = DateTime.UtcNow
                    };

                    if (!string.Equals(conn.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        db.DeleteConnection(conn.Name);
                    }

                    db.UpsertConnection(connItem);
                    connections.Value = db.GetConnections();
                    isOpen.Value = false;
                }
                catch (Exception ex)
                {
                    editError.Value = ex.Message;
                }
                finally
                {
                    isEditSaving.Value = false;
                }
            }

            return new Sheet(
                onClose: () => isOpen.Value = false,
                content: Layout.Vertical()
                    | nameField
                    | tokenField
                    | secretsInfo
                    | permissionsField
                    | (editError.Value != null ? Text.Danger(editError.Value) : null)
                    | new Button("Save Changes")
                        .Primary()
                        .Loading(isEditSaving.Value)
                        .OnClick(() => _ = SaveEditConnection()),
                title: $"Edit {conn.Name}",
                description: $"Modify configuration for {conn.Provider}"
            ).Width(Size.Rem(24));
        });

        string GetTokenValue(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return "";
            if (connectionString.TrimStart().StartsWith('{'))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(connectionString);
                    if (doc.RootElement.TryGetProperty("Token", out var tokenProp))
                    {
                        return tokenProp.GetString() ?? "";
                    }
                }
                catch { }
            }
            return connectionString;
        }

        async Task TestConnection(string connName)
        {
            var dict = new Dictionary<string, string>(testStatuses.Value);
            dict[connName] = "Testing...";
            testStatuses.Value = dict;

            var conn = db.GetConnectionByName(connName);
            if (conn == null)
            {
                dict = new Dictionary<string, string>(testStatuses.Value);
                dict[connName] = "Error: Not found";
                testStatuses.Value = dict;
                return;
            }

            var (success, errorMsg) = await executor.TestConnectionAsync(conn);
            
            dict = new Dictionary<string, string>(testStatuses.Value);
            if (success)
                dict[connName] = "Success";
            else
                dict[connName] = errorMsg;
            testStatuses.Value = dict;
        }

        void DeleteConnection(string connName)
        {
            db.DeleteConnection(connName);
            connections.Value = db.GetConnections();
        }

        async Task AddConnection()
        {
            var name = (newName.Value ?? "").Trim();
            var provider = formProvider.Value;
            var configText = (newConfig.Value ?? "").Trim();
            var perms = (newPermissions.Value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                formError.Value = "Name is required.";
                return;
            }
            if (provider == "Custom")
            {
                provider = (customProviderType.Value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(provider))
                {
                    formError.Value = "Provider Type is required for custom connections.";
                    return;
                }
            }
            if (string.IsNullOrWhiteSpace(configText))
            {
                formError.Value = "Token/Secret is required.";
                return;
            }
            if (db.GetConnectionByName(name) != null)
            {
                formError.Value = $"A connection with name '{name}' already exists.";
                return;
            }

            isSaving.Value = true;
            formError.Value = null;

            try
            {
                var configJson = configText;
                if (!configText.StartsWith('{'))
                {
                    try
                    {
                        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
                        var dict = deserializer.Deserialize<Dictionary<string, object>>(configText);
                        if (dict != null && dict.Count > 0)
                        {
                            configJson = System.Text.Json.JsonSerializer.Serialize(dict);
                        }
                        else
                        {
                            configJson = System.Text.Json.JsonSerializer.Serialize(new { Token = configText });
                        }
                    }
                    catch
                    {
                        configJson = System.Text.Json.JsonSerializer.Serialize(new { Token = configText });
                    }
                }

                var connItem = new ConnectionItem
                {
                    Name = name,
                    Provider = provider,
                    ConnectionString = configJson,
                    Permissions = string.IsNullOrWhiteSpace(perms) ? "*" : perms,
                    Created = DateTime.UtcNow,
                    Updated = DateTime.UtcNow
                };

                db.UpsertConnection(connItem);
                connections.Value = db.GetConnections();
                
                newName.Value = "";
                newConfig.Value = "";
                newPermissions.Value = "*";
                customProviderType.Value = "";
                selectedSection.Value = TagMyConnections;
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

        async Task SubmitRequest()
        {
            if (string.IsNullOrWhiteSpace(requestDescription.Value)) return;

            isSubmittingRequest.Set(true);
            requestError.Set(null);
            try
            {
                var service = new BugReportService(config);
                var files = new List<BugReportService.BugReportFile>();
                var configSanitized = service.CollectSanitizedConfig();
                if (configSanitized != null)
                {
                    files.Add(configSanitized);
                }

                var title = $"[Integration Request] {requestDescription.Value.Split('\n')[0]}";
                var body = $"Integration Request Details:\n\n{requestDescription.Value}";

                var result = await service.SubmitReportAsync(body, files, requestGithubUser.Value);

                if (result != null)
                {
                    client.Toast($"Integration request submitted successfully! ID: #{result.ReportId}", "Submitted", variant: ToastVariant.Success);
                    isRequestOpen.Set(false);
                    requestDescription.Value = "";
                    requestGithubUser.Value = "";
                }
                else
                {
                    requestError.Set("Failed to submit request to tendril services.");
                }
            }
            catch (Exception ex)
            {
                requestError.Set(ex.Message);
            }
            finally
            {
                isSubmittingRequest.Set(false);
            }
        }

        // --- SUB-SIDEBAR NAVIGATION ---
        var menuItems = new[]
        {
            MenuItem.Default("Integrations")
                .Icon(Icons.Plug)
                .Expanded()
                .Children(
                    MenuItem.Default("My Connections", TagMyConnections).Icon(Icons.Plug),
                    MenuItem.Default("Add Integration", TagCatalog).Icon(Icons.Plus)
                )
        };

        void OnSelect(Event<SidebarMenu, object> @event)
        {
            if (@event.Value is not string tag) return;
            selectedSection.Set(tag);
            if (tag == TagCatalog)
            {
                activeStep.Set("catalog");
            }
        }

        var sidebar = new SidebarMenu(OnSelect, menuItems);

        // --- VIEW 1: MY CONNECTIONS ---
        var connectionList = Layout.Vertical();
        var allConns = connections.Value;

        if (allConns.Count == 0)
        {
            connectionList = connectionList 
                | new Box(
                    Layout.Vertical()
                       | Icons.Plug.ToIcon().Color(Colors.Muted)
                       | Text.H3("No Connections Configured").Bold()
                       | Text.P("Set up integrations like Slack, Discord, or GitHub to enable automated agent runs.").Small().Muted()
                       | new Button("Add Connection")
                           .Primary()
                           .OnClick(() =>
                           {
                               selectedSection.Value = TagCatalog;
                               activeStep.Value = "catalog";
                           })
                       | new Separator()
                       | (Layout.Horizontal().AlignContent(Align.Left)
                          | Text.Muted("Have an integration idea?").Small()
                          | new Button("Create a feature request!")
                              .Variant(ButtonVariant.Outline)
                              .Small()
                              .OnClick(() => isRequestOpen.Set(true))
                         )
                );
        }
        else
        {
            var filteredConns = allConns
                .Where(c => string.IsNullOrEmpty(searchQuery.Value)
                            || c.Name.Contains(searchQuery.Value, StringComparison.OrdinalIgnoreCase)
                            || c.Provider.Contains(searchQuery.Value, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var searchBar = searchQuery.ToTextInput("Search connections...")
                .Prefix(Icons.Search.ToIcon().Color(Colors.Muted));

            if (filteredConns.Count == 0)
            {
                connectionList = connectionList 
                    | searchBar
                    | new Separator()
                    | new NoResultsView()
                    | new Separator()
                    | (Layout.Horizontal().AlignContent(Align.Left)
                       | Text.Muted("Have an integration idea?").Small()
                       | new Button("Create a feature request!")
                           .Variant(ButtonVariant.Outline)
                           .Small()
                           .OnClick(() => isRequestOpen.Set(true))
                      );
            }
            else
            {
                var grid = Layout.Grid().Columns(3);
                
                foreach (var conn in filteredConns)
                {
                    var connName = conn.Name;
                    testStatuses.Value.TryGetValue(connName, out var status);
                    
                    var statusBadge = status switch
                    {
                        "Testing..." => new Badge("Testing").Variant(BadgeVariant.Secondary),
                        "Success" => new Badge("Success").Variant(BadgeVariant.Success),
                        null or "" => null,
                        _ => new Badge("Failed").Variant(BadgeVariant.Destructive)
                    };

                    var providerIcon = conn.Provider.Equals("GitHub", StringComparison.OrdinalIgnoreCase) ? Icons.Github
                        : conn.Provider.Equals("Slack", StringComparison.OrdinalIgnoreCase) ? Icons.Slack
                        : conn.Provider.Equals("Discord", StringComparison.OrdinalIgnoreCase) ? Icons.Discord
                        : Icons.Plug;

                    grid = grid 
                        | new Box(
                            Layout.Vertical()
                               | Layout.Horizontal()
                                 | (Layout.Horizontal().AlignContent(Align.Left)
                                    | providerIcon.ToIcon().Color(Colors.Primary)
                                    | Layout.Vertical()
                                      | Text.H4(conn.Name).Bold().NoWrap().Overflow(Overflow.Ellipsis)
                                      | Text.Muted(conn.Provider).Small()
                                   )
                                 | statusBadge
                               | Text.P($"Permissions: {conn.Permissions}").Small().Muted()
                               | (status != null && status != "Testing..." && status != "Success" ? Text.Danger(status).Small() : null)
                               | (Layout.Horizontal().AlignContent(Align.Left)
                                 | new Button("Test").Small().OnClick(() => _ = TestConnection(connName))
                                 | new Button("Edit").Small().OnClick(() =>
                                   {
                                       editingConn.Value = conn;
                                       editName.Value = conn.Name;
                                       editConfig.Value = GetTokenValue(conn.ConnectionString);
                                       editPermissions.Value = conn.Permissions;
                                       editError.Value = null;
                                       triggerEdit(conn);
                                   })
                                 | new Button("Delete").Small().Variant(ButtonVariant.Destructive).OnClick(() => DeleteConnection(connName))
                                 )
                          );
                }

                connectionList = connectionList 
                    | searchBar
                    | new Separator()
                    | grid;
            }
        }

        // --- VIEW 2: ADD INTEGRATION (CATALOG & DYNAMIC FORM) ---

        var availableProviders = providers.Select(p =>
        {
            Icons icon;
            if (string.Equals(p.Icon, "Slack", StringComparison.OrdinalIgnoreCase))
                icon = Icons.Slack;
            else if (string.Equals(p.Icon, "Discord", StringComparison.OrdinalIgnoreCase))
                icon = Icons.Discord;
            else if (string.Equals(p.Icon, "Github", StringComparison.OrdinalIgnoreCase))
                icon = Icons.Github;
            else
                icon = Icons.Plug;

            return (Provider: p.ProviderName ?? "", Description: p.Description ?? "", Icon: icon);
        }).ToList();

        availableProviders.Add((Provider: "Custom", Description: "Configure a custom connection with custom secrets and configuration.", Icon: Icons.Plug));

        var filteredProviders = availableProviders
            .Where(p => string.IsNullOrEmpty(catalogSearchQuery.Value)
                        || p.Provider.Contains(catalogSearchQuery.Value, StringComparison.OrdinalIgnoreCase)
                        || p.Description.Contains(catalogSearchQuery.Value, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var catalogSearchBar = catalogSearchQuery.ToTextInput("Search integrations catalog...")
            .Prefix(Icons.Search.ToIcon().Color(Colors.Muted));

        object CatalogCard(string provider, string description, Icons icon)
        {
            return new Box(
                Layout.Vertical()
                   | icon.ToIcon().Color(Colors.Primary)
                   | Text.H3(provider).Bold()
                   | Text.Muted(description).Small()
                   | new Button("Configure")
                       .Primary()
                       .OnClick(() =>
                       {
                           formProvider.Value = provider;
                           newName.Value = "";
                           newConfig.Value = "";
                           customProviderType.Value = "";
                           newPermissions.Value = provider switch
                           {
                               "Slack" => "send-message, add-reaction",
                               "Discord" => "send-message",
                               "GitHub" => "create-pr, comment-pr",
                               _ => "*"
                           };
                           formError.Value = null;
                           activeStep.Value = "form";
                       })
            );
        }

        var catalogContent = Layout.Vertical();

        if (filteredProviders.Count == 0)
        {
            catalogContent = catalogContent
                | catalogSearchBar
                | new Separator()
                | new NoResultsView()
                | new Separator()
                | (Layout.Horizontal().AlignContent(Align.Left)
                   | Text.Muted("Have an integration idea?").Small()
                   | new Button("Create a feature request!")
                       .Variant(ButtonVariant.Outline)
                       .Small()
                       .OnClick(() => isRequestOpen.Set(true))
                  );
        }
        else
        {
            var catalogGrid = Layout.Grid().Columns(3);
            foreach (var p in filteredProviders)
            {
                catalogGrid = catalogGrid | CatalogCard(p.Provider, p.Description, p.Icon);
            }

            catalogContent = catalogContent
                | catalogSearchBar
                | new Separator()
                | catalogGrid;
        }

        var tokenLabel = formProvider.Value switch
        {
            "Slack" => "OAuth Bot Token",
            "Discord" => "Bot Token",
            "GitHub" => "Personal Access Token (PAT)",
            _ => "API Token / Secrets (YAML/JSON or raw value)"
        };

        var tokenPlaceholder = formProvider.Value switch
        {
            "Slack" => "starts with xoxb-",
            "Discord" => "Bot token from Discord Developer Portal",
            "GitHub" => "starts with ghp_ or github_pat_",
            _ => "token: value\nsecret: value"
        };

        var permissionsHelp = formProvider.Value switch
        {
            "Slack" => "Allowed actions. E.g. send-message, add-reaction or * for all.",
            "Discord" => "Allowed actions. E.g. send-message or * for all.",
            "GitHub" => "Allowed actions. E.g. create-pr, comment-pr or * for all.",
            _ => "Allowed actions (comma-separated)."
        };

        var nameField = newName.ToTextInput("e.g. production-alerts")
            .WithField()
            .Label("Connection Name")
            .Required();

        var tokenField = formProvider.Value == "Custom"
            ? (object)newConfig.ToTextareaInput()
                .Placeholder(tokenPlaceholder)
                .Rows(4)
                .WithField()
                .Label(tokenLabel)
                .Required()
            : newConfig.ToTextInput(tokenPlaceholder)
                .WithField()
                .Label(tokenLabel)
                .Required();

        var permissionsField = newPermissions.ToTextInput("e.g. *")
            .WithField()
            .Label("Permissions")
            .Description(permissionsHelp);

        var customProviderField = formProvider.Value == "Custom"
            ? (object)customProviderType.ToTextInput("e.g. Linear")
                .WithField()
                .Label("Provider Type")
                .Required()
            : new Fragment();

        var secretsInfo = formProvider.Value switch
        {
            "Slack" => (object)(Layout.Vertical()
                | (Layout.Horizontal().AlignContent(Align.Left)
                   | Text.Muted("Need a Slack Bot Token?").Small()
                   | new Button("Go to Slack App Console")
                       .Variant(ButtonVariant.Ghost)
                       .Small()
                       .OnClick(() => navigator.Navigate("https://api.slack.com/apps"))
                  )
                | Text.Muted("Create an app in your Slack workspace, enable 'bots' features, install it, and copy the Bot User OAuth Token (xoxb-...).").Small()),
            "Discord" => (object)(Layout.Vertical()
                | (Layout.Horizontal().AlignContent(Align.Left)
                   | Text.Muted("Need a Discord Bot Token?").Small()
                   | new Button("Go to Discord Developer Portal")
                       .Variant(ButtonVariant.Ghost)
                       .Small()
                       .OnClick(() => navigator.Navigate("https://discord.com/developers/applications"))
                  )
                | Text.Muted("Create an application, add a Bot under the Bot tab, click 'Reset Token' to generate, and copy the Bot Token.").Small()),
            "GitHub" => (object)(Layout.Vertical()
                | (Layout.Horizontal().AlignContent(Align.Left)
                   | Text.Muted("Need a GitHub Personal Access Token?").Small()
                   | new Button("Go to GitHub Tokens Settings")
                       .Variant(ButtonVariant.Ghost)
                       .Small()
                       .OnClick(() => navigator.Navigate("https://github.com/settings/tokens"))
                  )
                | Text.Muted("Generate a Personal Access Token (Classic or Fine-grained) with 'repo' scope (and 'workflow' if editing workflows) and copy the token (ghp_...).").Small()),
            _ => new Fragment()
        };

        var formContent = new Box(
            Layout.Vertical()
               | Layout.Horizontal().AlignContent(Align.Left)
                 | new Button("Back").Variant(ButtonVariant.Outline).OnClick(() => activeStep.Value = "catalog")
                 | Text.H3($"Configure {formProvider.Value}").Bold()
               | new Separator()
               | customProviderField
               | nameField
               | tokenField
               | secretsInfo
               | permissionsField
               | (formError.Value != null ? Text.Danger(formError.Value) : null)
               | new Button("Save Connection")
                   .Primary()
                   .Loading(isSaving.Value)
                   .OnClick(() => _ = AddConnection())
        );

        object mainContent = selectedSection.Value switch
        {
            TagMyConnections => connectionList,
            TagCatalog => activeStep.Value == "catalog" ? catalogContent : formContent,
            _ => connectionList
        };

        var sections = new[]
        {
            (Tag: TagMyConnections, Label: "My Connections"),
            (Tag: TagCatalog, Label: "Add Integration")
        };
        var currentLabel = sections.FirstOrDefault(s => s.Tag == selectedSection.Value).Label;

        var mobileHeader = MobileItemPicker.Build(
                currentLabel,
                sections.ToList(),
                s => s.Label,
                s => s.Tag == selectedSection.Value,
                s => selectedSection.Set(s.Tag))
            .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);

        var contentWithMobileHeader = Layout.Vertical().Height(Size.Full())
                                      | mobileHeader
                                      | (Layout.Vertical().Height(Size.Grow()) | mainContent);

        object dialog = isRequestOpen.Value ? new Dialog(
            _ => isRequestOpen.Set(false),
            new DialogHeader("Request Integration"),
            new DialogBody(
                Layout.Vertical()
                | Text.Muted("Let us know what integration you want us to add. We will submit a feature request to our team.")
                | requestDescription.ToTextareaInput()
                    .Placeholder("Name of the app/service (e.g. Linear, Notion, etc.) and what actions you want to perform...")
                    .Rows(4)
                    .Disabled(isSubmittingRequest.Value)
                | requestGithubUser.ToTextInput()
                    .Placeholder("Your GitHub Username (Optional)")
                    .Disabled(isSubmittingRequest.Value)
                | (requestError.Value != null ? Text.Danger(requestError.Value) : null)
            ),
            new DialogFooter(
                new Button("Cancel").Variant(ButtonVariant.Ghost).OnClick(() => isRequestOpen.Set(false)).Disabled(isSubmittingRequest.Value),
                new Button("Submit Request").Primary().OnClick(async () => await SubmitRequest())
                    .Disabled(isSubmittingRequest.Value || string.IsNullOrWhiteSpace(requestDescription.Value))
                    .Loading(isSubmittingRequest.Value)
            )
        ) : new Fragment();

        var sidebarLayout = new SidebarLayout(contentWithMobileHeader, sidebar);

        return Layout.Vertical().Height(Size.Full()).RemoveParentPadding()
            | sidebarLayout
            | dialog
            | editSheetView;
    }
}
