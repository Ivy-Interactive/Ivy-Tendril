---
searchHints:
  - tutorial
  - walkthrough
  - quickstart
  - first-plan
  - beginner
  - example
  - end-to-end
icon: GraduationCap
---

# Tutorial

<Ingress>
A complete end-to-end walkthrough: install Tendril, configure a local repository, create your first plan, execute it with an agent, and review the result.
</Ingress>

## Step 1 — Install Tendril

### macOS / Linux

```bash
curl -sSf https://raw.githubusercontent.com/Ivy-Interactive/Ivy-Tendril/main/src/install.sh | sh
```

### Windows

```powershell
Invoke-RestMethod -Uri https://raw.githubusercontent.com/Ivy-Interactive/Ivy-Tendril/main/src/install.ps1 | Invoke-Expression
```

The installer sets up the `tendril` binary and required dependencies. You can also install via .NET:

```bash
dotnet tool install -g Ivy.Tendril
```

<Callout type="Tip">
If you use `dotnet tool install`, make sure PowerShell 7, Git, and `gh` CLI are already installed.

</Callout>

Verify everything is working:

```bash
tendril doctor
```

This checks your environment, required software, database, and agent connectivity. Fix any issues it reports before continuing.

<Callout type="Tip">
Use `tendril doctor --verbose` to see detailed diagnostics if something isn't working as expected.
</Callout>

## Step 2 — Start Tendril

```bash
tendril
```

This launches the web server at `https://localhost:5010`. Open it in your browser — you'll see the Tendril dashboard.

## Step 3 — Set Up Your Repository

You need a local git repository for Tendril to work with. If you don't have one handy, clone any project:

```bash
git clone https://github.com/your-org/your-repo.git
```

Now register it in Tendril. Open **Settings** from the dashboard and navigate to **Projects**, or edit `TENDRIL_HOME/config.yaml` directly:

```yaml
codingAgent: claude

projects:
  - name: MyProject
    repo: D:\Repos\MyProject
    verifications:
      - DotnetBuild
      - CheckResult
```

Set `codingAgent` to the agent you want to use (`claude`, `codex`, `gemini`, etc.) and adjust `verifications` to match your stack.

<Callout type="Tip">
Add a `CLAUDE.md` or `AGENTS.md` file to your repo root with project conventions — Tendril passes these to the agent as context.

</Callout>

## Step 4 — Create a Plan

From the dashboard, click **New Plan** and describe what you want to build or fix. Tendril will run the `CreatePlan` promptware to draft a structured plan with a problem statement, proposed solution, and verification checklist.

You can also create a plan from the CLI:

```bash
tendril plan create my-first-plan "Add a health-check endpoint"
tendril plan add-repo my-first-plan "D:\Repos\MyProject"
```

Either way, the plan starts in **Draft** state. Open it in the dashboard to review the drafted revision — this is your chance to refine the scope before execution.

## Step 5 — Execute the Plan

Once you're happy with the draft, click **Execute** in the dashboard. Tendril's `ExecutePlan` promptware will:

1. Create a **git worktree** — an isolated checkout so your working branch stays untouched
2. Read the plan context, repo conventions, and revision details
3. Implement the changes
4. Run your configured **verifications** (build, test, format)

Monitor progress in real time from the **Jobs** view. You can see status updates, token usage, and cost tracking as the agent works.

If the agent succeeds and verifications pass, the plan moves to **ReadyForReview**.

<Callout type="Info">
If execution fails, the plan moves to **Failed**. Check the logs, fix any blockers, and retry — the worktree is preserved so the agent can pick up where it left off.

</Callout>

## Step 6 — Review the Result

Open the plan in the dashboard. From the review screen you can:

- **Browse the diff** — see exactly what the agent changed
- **Check verifications** — build, test, and format results
- **Read the execution log** — full agent output for transparency
- **View recommendations** — suggestions the agent flagged during execution

If the changes look good, **approve** the plan. Tendril can then open a pull request via the `CreatePr` promptware, moving the plan to **Completed**.

If something needs work, send the plan back to **Draft** to refine and re-execute.

## What Just Happened?

You walked through the full Tendril loop:

```
Draft → Building → Executing → ReadyForReview → Completed
```

The agent worked in an isolated worktree, ran your verification suite, and the result is a reviewable diff — all tracked in a single plan folder on disk.

## Next Steps

- Learn more about [Plans](../02_Concepts/01_Plans.md) and plan states
- Explore [Promptwares](../02_Concepts/02_Promptwares.md) to customize agent behavior
- Configure [Projects](../03_Configuration/02_Projects.md) with per-repo verification rules
- See the full [CLI reference](../07_Advanced/01_CLI.md) for power-user workflows
