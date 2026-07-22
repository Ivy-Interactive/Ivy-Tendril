# Ivy Tendril - Static Prototype Mockup

This directory contains a high-fidelity static HTML, CSS, and JavaScript prototype concept for the Ivy Tendril multi-agent workspace.

## Feature Overview & Interactive Flow

1. **Updated Sidebar Navigation**:
   - **Orchestrate**: Chat, Workflows, Jobs, Recs
   - **Review**: Plans, Drafts
   - **Overview**: Dashboard, PR's
   - **Other**: Help

2. **Split-View Concept**:
   - **Left Pane**: Agent chat interface with quick prompts, model tag, and interactive feedback cards.
   - **Right Pane**: Live streamed workflow DAG diagram showing active subagents, connecting edges, and status indicators.

3. **Interactive Demo Storyline**:
   - Click **Step 1 (Prompt & Plan)**: Submit prompt `make me Jira` or click quick prompt chip.
   - Watch **Step 2 (Agent Flow)**: 5 parallel subagents spin up on the right-hand canvas graph.
   - Experience **Step 3 (User Feedback)**: Backend API agent encounters a blocking decision card. Selecting a workflow model unblocks the downstream flow.
   - Inspect **Jobs App**: Emulated background promptware execution table and streaming terminal logs.
   - Review **Step 4 (Draft & Tunnel)**: Switch to Drafts app to inspect generated project artifacts and click **Spin Up Repo in Tunnel** to open a live interactive preview of the generated Jira application.

## Quick Start

Open `index.html` in any browser or launch a local web server:

```bash
# Option 1: Direct open on macOS
open mockup/index.html

# Option 2: Serve via npx
npx serve mockup
```
