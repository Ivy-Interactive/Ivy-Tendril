---
searchHints:
  - jobs
  - running
  - execution
  - agents
  - status
icon: Activity
---

# Jobs

<Ingress>
Running and past promptware runs: status, cost, duration, and live output.
</Ingress>

## Overview

Any dispatched promptware (Execute from Drafts, Revise from Review, …) shows up as a job with:

- **Status** — `Running`, `Completed`, `Failed`, `Pending`, …
- **Type** — e.g. `CreatePlan`, `ExecutePlan`, `UpdatePlan`, `CreatePr`
- **Tokens** — Usage vs. your provider quota

## Live output

Built-in terminal shows **stdout/stderr** from the agent (builds, logs, errors)—not only a spinner.

## Controls

| Action | Effect |
|--------|--------|
| **Stop** | End the run and return the plan to its previous state. The work product (worktree) is kept so you can resume. |
| **Delete** | Remove the job from history. For an `ExecutePlan` job this also discards its work product and resets the plan to `Draft`. |
| **Logs** | Open `logs/` for that plan. |
| **Retry** | Re-run when a transition is stuck. |
