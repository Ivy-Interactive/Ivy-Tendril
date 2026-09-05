---
searchHints:
  - vault
  - sync
  - import
  - push
  - catalog
  - discover
  - connect
  - auto-sync
---

# vault

<Ingress>
Manage team configuration vaults, discover and connect shared repositories on GitHub, inspect catalog assets, import projects, and publish configuration updates directly from the CLI.
</Ingress>

## Commands

```terminal
>tendril vault list [--json]
>tendril vault status [vault-id] [--json]
>tendril vault discover [--json]
>tendril vault connect <repo-url> [--name <custom-name>]
>tendril vault create <repo-name> [--public] [--org <org>]
>tendril vault disconnect [vault-id]
>tendril vault sync [vault-id]
>tendril vault pull [vault-id]
>tendril vault set-auto-sync <enabled> [--vault <vault-id>]
>tendril vault catalog [vault-id] [--json]
>tendril vault import <project-name> [options]
>tendril vault push <projects...> [options]
>tendril vault delete <project-name> [--vault <vault-id>]
```

## Vault Management

#### list

```terminal
>tendril vault list
>tendril vault list --json
```

Lists all configured vaults, displaying their ID, name, remote repository URL, current branch, commits ahead/behind, last sync time, and auto-sync state.

#### status

```terminal
>tendril vault status
>tendril vault status <vault-id>
>tendril vault status --json
```

Shows detailed diagnostic and synchronization status for a specific vault or the primary configured vault.

#### discover

```terminal
>tendril vault discover
>tendril vault discover --json
```

Scans GitHub using the GitHub CLI (`gh`) to discover existing vault repositories accessible to your account and organizations.

#### connect

```terminal
>tendril vault connect https://github.com/my-org/team-vault.git
>tendril vault connect my-org/team-vault --name "Engineering Vault"
```

Connects an existing Git repository as a team vault.

#### create

```terminal
>tendril vault create engineering-vault
>tendril vault create team-vault --org my-org --public
```

Creates a new repository on GitHub (private by default), initializes it as a vault, and connects it locally.

#### disconnect

```terminal
>tendril vault disconnect
>tendril vault disconnect <vault-id>
```

Disconnects a vault from Tendril without deleting local project files.

#### sync / pull

```terminal
>tendril vault sync
>tendril vault pull
>tendril vault sync <vault-id>
```

Pulls the latest configuration changes from the remote vault repository and updates tracked local projects.

#### set-auto-sync

```terminal
>tendril vault set-auto-sync true
>tendril vault set-auto-sync false --vault <vault-id>
```

Enables or disables automatic synchronization for a vault. Accepts `true`, `false`, `1`, `0`, `yes`, `no`.

## Catalog & Project Sharing

#### catalog

```terminal
>tendril vault catalog
>tendril vault catalog <vault-id> --json
```

Lists all projects and asset counts (repositories, custom skills, MCP servers, memories, review actions, verifications) published in the vault.

#### import

```terminal
>tendril vault import MyProject
>tendril vault import MyProject --target-name LocalProject --merge
>tendril vault import MyProject --repo api=~/code/api --repo web=~/code/web
```

Imports a project from the vault catalog into local Tendril configuration.

| Option | Description |
|--------|-------------|
| `--target-name <name>` | Custom local project name |
| `--vault <vault-id>` | Vault ID or name to import from |
| `--repo <name=path>` | Local path mapping for a repository (repeatable) |
| `--no-permissions` | Skip importing security and permissions rules |
| `--merge` | Merge into an existing local project instead of replacing |

#### push

```terminal
>tendril vault push MyProject
>tendril vault push ProjectA ProjectB --version "1.2.0" --changelog "Added new skills and verifications"
>tendril vault push MyProject --reviewer alice,bob --title "feat(vault): update MyProject"
```

Collects project configuration, custom skills, MCP servers, memories, review actions, and verifications, commits them to a new branch, and creates a pull request to the vault repository.

| Option | Description |
|--------|-------------|
| `--vault <vault-id>` | Target vault ID |
| `--version <version>` | Custom version string (defaults to timestamp) |
| `--changelog <text>` | Changelog notes included in PR description |
| `--title <title>` | Pull request title |
| `--body <body>` | Pull request description |
| `--reviewer <names>` | Reviewer username(s) to assign on GitHub |

#### delete

```terminal
>tendril vault delete OldProject
>tendril vault delete OldProject --vault <vault-id>
```

Deletes a project from the vault repository and creates a pull request on GitHub to apply the deletion.

## Examples

**Connect and sync a team vault:**

```terminal
># Discover team vaults on GitHub
>tendril vault discover

># Connect vault
>tendril vault connect https://github.com/my-org/shared-vault.git

># Pull updates
>tendril vault sync
```

**Import a project from the catalog:**

```terminal
># Inspect available catalog projects
>tendril vault catalog

># Import with custom repository paths
>tendril vault import BackendService --repo backend=D:/Projects/backend
```

**Publish project updates:**

```terminal
># Push changes and open a pull request
>tendril vault push BackendService --changelog "Added Playwright E2E verification"
```
