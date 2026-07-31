# Ivy.Tendril.Plugin.Linear

## StrawberryShake GraphQL Client

This plugin uses [StrawberryShake](https://chillicream.com/docs/strawberryshake) for typed GraphQL client generation.

- **Schema**: `schema.graphql` (Linear's full schema)
- **Queries**: `Queries.graphql` (the operations this plugin uses)
- **Generated code**: `Generated/LinearGraphQLClient.Client.cs`

### Regenerating the client after query changes

After modifying `Queries.graphql` (or `schema.graphql`), you must regenerate the typed client:

```sh
cd src/plugins/plugins/Ivy.Tendril.Plugin.Linear
dotnet-graphql generate
```

This updates `Generated/LinearGraphQLClient.Client.cs`. The generated file should be committed alongside your query changes.

Note: `dotnet build` alone does NOT regenerate the client — you must run the CLI tool explicitly.

## Linear Object Model (key relationships)

Understanding these relationships matters when building filters:

- **Workflow States** belong to a specific team. Different teams can have states with the same name but they are distinct entities. Filter issues by state *name* (not ID) so it works across teams.
- **Labels** can be workspace-level (`team` is null, available to all teams) or team-scoped (only applicable to that team's issues). Labels can also be groups (`isGroup: true`) that contain child labels and can't be applied directly.
- **Projects** span multiple teams — a project can contain issues from different teams.
- **Users** have team memberships, but assignment to an issue is not strictly enforced to that issue's team members at the API level.
- **Priority** is a simple integer (0=None, 1=Urgent, 2=High, 3=Medium, 4=Low), universal across all teams/projects.

