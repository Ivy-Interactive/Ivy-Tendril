---
searchHints:
  - troubleshooting
  - fix
  - error
  - problem
  - symptom
  - debug
  - diagnose
icon: Wrench
---

# Troubleshooting

<Ingress>
Common issues and how to fix them. If you're still stuck, run <code>tendril doctor</code> and reach out on <a href="https://discord.gg/FHgxkDga3y">Discord</a>.
</Ingress>

## Installation & Environment

| Symptom | Fix |
|---------|-----|
| `TENDRIL_HOME` not found | Set the environment variable and restart your terminal |
| `gh` not authenticated | Run `gh auth login` |
| `pwsh` not found | Install [PowerShell 7](https://github.com/PowerShell/PowerShell) — required on all platforms |
| `git` not found | Install Git and ensure it's on your `PATH` |
| `pandoc` not found | Optional — install it if you need document conversion features |
| `tendril` command not recognized | Ensure `~/.dotnet/tools` (or equivalent) is on your `PATH`, then retry |

## Plans

| Symptom | Fix |
|---------|-----|
| Plan stuck in `Draft` | Verify a repo is attached (`tendril plan get <id>`) and the project exists in `config.yaml` |
| `YAML:Missing` or `YAML:Empty` health codes | Run `tendril plan doctor --fix` to scaffold missing plan files |
| Plan has no repos configured | Add one with `tendril plan add-repo <id> <path>` |
| Plan folder is corrupted | Run `tendril plan doctor --fix` — it repairs missing fields and stale worktrees |
| Empty/junk plan folders | Run `tendril plan doctor --prune` to clean them up |

## Execution & Agents

| Symptom | Fix |
|---------|-----|
| Agent not reachable | Check `codingAgent` in `config.yaml` and verify your API key / credentials |
| Execution fails immediately | Read the job log — common causes are missing repo context or misconfigured verifications |
| Verifications keep failing | Run the verification commands manually in the worktree to isolate the issue |
| Stale worktree after a failed run | Run `tendril plan cleanup <id> --force` to remove it |
| Nested worktree detected | Run `tendril plan doctor --fix` to remove nested artifacts |

## Database

| Symptom | Fix |
|---------|-----|
| Schema version mismatch | Run `tendril db-migrate` to apply pending migrations |
| Database corrupted | Run `tendril db-reset --force` to start fresh (plan files on disk are not affected) |
| Can't determine schema version | Run `tendril db-version` — if it errors, the database file may be locked or missing |

<Callout type="Tip">
When reporting an issue, include the output of `tendril doctor` and `tendril version` — it helps the team diagnose problems faster.

</Callout>
