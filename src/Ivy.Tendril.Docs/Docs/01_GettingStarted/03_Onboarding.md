---
icon: ClipboardCheck
searchHints:
  - onboarding
  - checklist
  - prepare
  - prerequisites
  - dev machine
  - environment
  - worktree
  - agents.md
  - gh
  - git
  - mcp
  - setup
---

# Onboarding a Codebase

<Ingress>
A checklist for preparing your dev machine and your repository so Tendril can plan, execute, verify, and ship changes unattended.
</Ingress>

Tendril runs a coding agent against your repository inside an isolated git worktree, then builds, tests, and opens a pull request. For that loop to succeed without a human in the seat, the machine and the repo have to be set up ahead of time. Work through the checklist below once per machine and once per repository.

<Callout type="Tip">
After you finish, run `tendril doctor` to confirm your environment, required software, database, and agent connectivity are all green.
</Callout>

## Machine checklist

### 1. Required build software is installed

Every tool needed to compile the project must be on the machine and on `PATH`. The agent cannot install a missing SDK for you mid-run. For a .NET repo that usually means the .NET SDK; for a Node repo, Node and the package manager; plus anything your build scripts shell out to.

<Callout type="Info">
The bar is: a fresh clone builds from a clean shell with the documented commands, no manual clicks or IDE-only steps.
</Callout>

### 2. The preferred coding CLI is installed and authenticated

Install the agent you set as `codingAgent` and log in so it runs non-interactively. See [Coding Agents](../06_CodingAgents/_Index.md) for the exact command per agent.

```bash
# Example: Claude Code
npm install -g @anthropic-ai/claude-code
claude login
```

Verify the CLI is on `PATH` (`claude`, `codex`, `copilot`, `gemini`, or `opencode`) and that a plain invocation does not stop to prompt for a login.

### 3. Git is installed and authorized for unattended use

Tendril pulls code, creates worktrees, commits, and pushes on your behalf. Confirm all of that works without an interactive prompt:

- A global identity is set (`git config --global user.name` and `user.email`).
- Credentials are cached or a key is loaded, so `git pull` and `git push` never ask for a password.
- Worktrees can be created and removed (`git worktree add` / `git worktree remove`).

<Callout type="warning">
If pushing over HTTPS still prompts, configure a credential helper or use an SSH key with a passphrase-less agent. A single interactive prompt will stall an otherwise successful run.
</Callout>

### 4. The GitHub CLI is installed and authenticated

`CreatePr` uses `gh` to open pull requests. Install it and authenticate:

```bash
gh auth login
gh auth status  
```

### 5. Required MCP servers are installed globally

If you rely on MCP servers (for example Jira for issue context, or Figma for designs), install and register them at the user/global level so every worktree can reach them. MCP servers are configured on the coding agent, not inside Tendril.

```bash
# Example: register an MCP server globally for Claude Code
claude mcp add --scope user jira -- npx -y @your-org/jira-mcp
claude mcp add --scope user figma -- npx -y figma-developer-mcp
claude mcp list   # verify they are reachable
```

<Callout type="Info">
Use the global / user scope (not project scope) so servers survive the ephemeral worktree the agent runs in. Store any tokens the servers need as environment variables on the machine.
</Callout>

Make sure you are actually authenticated to each MCP, not just that the servers are registered. The most reliable check is end to end: run the coding agent from inside Tendril (execute a small plan, or open the agent for a plan) and confirm every MCP comes up authenticated rather than prompting for a login or returning auth errors. Some servers only complete their OAuth flow on first use, so verifying through Tendril catches an unauthenticated server before it stalls a real run.

## Repository checklist

### 6. The repo is worktree-ready

`ExecutePlan` works in a fresh `git worktree`, not your open checkout. A worktree starts from a clean tree with no `bin/`, `obj/`, `node_modules/`, `.env`, or restored packages. Make sure a brand-new worktree can build:

- List any steps needed after checkout before the code compiles (restore, generate, copy an example env file). Document them, and prefer a single script that performs them.
- Do not depend on files that are gitignored and only exist in your main checkout.
- Use a package manager with a shared cache so each worktree restores fast instead of downloading everything again (for example a global NuGet cache, pnpm store, or npm cache). 

<Callout type="Tip">
Quick test: `git worktree add ../repo-probe`, then run your documented build in that folder from a clean shell. If it builds, Tendril will too. Remove it with `git worktree remove ../repo-probe`.
</Callout>

### 7. Write a run script for each app

Give every app in the repo a small, committed script that starts it on configurable ports. Tendril can execute several plans across worktrees at once, so a hard-coded port makes the second instance fail to bind. Keep each port overridable and default it sensibly.

Most apps have a backend and a frontend, so start both, each on its own port. For a Vite frontend in front of a Python API the script could look like this:

```bash
#!/usr/bin/env bash
# run.sh - launch the Python backend API and the Vite frontend
set -euo pipefail

api_port="${API_PORT:-8000}"
web_port="${WEB_PORT:-5173}"

cd "$(dirname "$0")"

# Backend: set up the virtualenv and install dependencies.
python -m venv .venv
source .venv/bin/activate
pip install -q -r requirements.txt

# Start the backend API in the background on its own port.
uvicorn app.main:app --port "$api_port" &
api_pid=$!

# Stop the backend when the frontend exits.
trap 'kill "$api_pid" 2>/dev/null' EXIT

# Frontend: install and start the Vite dev server in the foreground. Vite
# auto-picks the next free port if this one is taken, so concurrent
# worktrees never collide.
npm --prefix web install --prefer-offline --no-audit
npm --prefix web run dev -- --port "$web_port" --open
```

<Callout type="Info">
Keeping the launch logic in a committed script (rather than a long inline command) means people and agents start the app the same way, and a fresh worktree can be run with a single command.
</Callout>

### 8. Add an AGENTS.md (or README.md) at the repo root

Give the agent the minimum context it needs to orient itself without guessing. At minimum, cover:

- **Prerequisites** the machine needs to build and run the code.
- **Apps in the codebase** and how they relate (for example: web frontend talks to an API which talks to a database).
- **How each app is compiled**, using commands that are obvious to an agent (a fresh clone should build from these alone).
- **How to run each app**, pointing at the run scripts from the previous step.

## Next steps

- Register the repo in [Project Setup](../03_Configuration/02_Projects.md) and wire up verifications in [Setup & Settings](../03_Configuration/01_Setup.md).
- Run the full loop end to end with the [Tutorial](04_Tutorial.md).
- Pick and configure your agent under [Coding Agents](../06_CodingAgents/_Index.md).
