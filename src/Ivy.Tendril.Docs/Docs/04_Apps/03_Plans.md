---
searchHints:
  - draft
  - plan
  - ideation
  - blocked
  - makeplan
icon: Feather
---

# Plans

<Ingress>
Plans in **Draft** (or **Blocked**): shape the work before execution (`PlansApp`).
</Ingress>

## Role

New plans start here. Refine the plan with the UI and promptwares before heavy coding.

## UI

- **Sidebar** — Filtered draft list; main pane shows the selected plan.
- **Content** — Latest revision markdown (problem, approach, tests).
- **Project** — Settings from `config.yaml` for that repo.
- **Git tab** — Appears for drafts that already have worktrees, commits or PRs (for example after a Revise from Review).

## Actions

1. **ExecutePlan** — Lock revision, create worktree, run the main execution agent.
2. **ExpandPlan** — Flesh out a thin plan with more implementation detail.
3. **Shelve to Icebox** — Move to **Icebox** to clear the draft list.

## Files on disk

Edits under `TENDRIL_HOME/Inbox` or in a plan folder in your editor sync back via filesystem watchers—no manual refresh needed.
