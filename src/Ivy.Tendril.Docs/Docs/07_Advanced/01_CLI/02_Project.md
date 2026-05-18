---
searchHints:
  - project
  - repo
  - verification
  - build
  - dependency
  - review
  - action
  - color
  - context
---

# project

<Ingress>
Manage projects stored in `config.yaml`. Projects group repositories, verifications, build dependencies, and review actions.
</Ingress>

## CRUD

```bash
tendril project list
tendril project get <name>
tendril project add <name> [--color <color>] [--context <text>]
tendril project remove <name>
tendril project set <name> <field> <value>
```

- **list** — shows name, color, repo count, verification count
- **get** — shows full details including repos, verifications, review actions, and build dependencies
- **set** — supported fields: `name`, `color`, `context`

## Repos

```bash
tendril project add-repo <project-name> <repo-path> [options]
tendril project remove-repo <project-name> <repo-path>
```

| Option | Effect |
|--------|--------|
| `--pr-rule` | PR rule (`default`, `yolo`) |
| `--base-branch` | Default base branch (e.g. `main`) |
| `--sync-strategy` | Worktree sync strategy (`fetch`, `pull`) |

## Verifications

```bash
tendril project add-verification <project-name> <verification-name> [--required]
tendril project remove-verification <project-name> <verification-name>
```

Use `--required` to mark the verification as mandatory for plan completion.

## Build Dependencies

```bash
tendril project add-build-dep <project-name> <dependency>
tendril project remove-build-dep <project-name> <dependency>
```

Build dependencies are checked before an agent starts executing a plan.

## Review Actions

```bash
tendril project add-review-action <project-name> <name> [--command <cmd>] [--condition <expr>]
tendril project remove-review-action <project-name> <name>
```

Review actions are shell commands run automatically during plan review. The action is skipped if `--condition` evaluates to false.

```bash
tendril project add-review-action Tendril RunTests \
  --command "dotnet test --no-build" \
  --condition 'Test-Path "tests/"'
```
