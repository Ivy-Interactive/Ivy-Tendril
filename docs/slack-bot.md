# Slack Bot

Tendril ships with a bundled Slack bot plugin. It lets you create plans, execute them, and receive job notifications directly from Slack. It connects over Slack Socket Mode (an outbound websocket), so it works from any machine — no public URL, tunnel, or webhook configuration required.

## Setup

1. Open the **Plugins** app in Tendril. The **Slack Bot** plugin is pre-installed and waiting for configuration.
2. Click **Create Slack App**. Slack opens in your browser with a fully pre-configured app manifest — bot user, `/tendril` slash command, permissions, and Socket Mode are all pre-filled. Pick your workspace and click **Create**, then **Install to Workspace**.
3. Copy two tokens into the wizard:
   - **Bot User OAuth Token** (`xoxb-…`) from *OAuth & Permissions*.
   - **App-Level Token** (`xapp-…`) from *Basic Information → App-Level Tokens* (generate one with the `connections:write` scope).
4. Click **Validate & Continue** — Tendril verifies both tokens against Slack.
5. Pick a channel for job notifications, optionally restrict who may run commands, and click **Save & Start Bot**.

The bot connects immediately and reconnects automatically whenever Tendril restarts.

## Commands

Use `/tendril` in any channel, mention the bot (`@tendril …`), or DM it:

| Command | Effect |
| --- | --- |
| `new <description>` | Create a plan from a description |
| `new project:Name <description>` | Create a plan in a specific project |
| `run <planId>` | Execute a plan |
| `plans [state]` | List recent plans, optionally filtered by state |
| `projects` | List configured projects |
| `status <jobId>` | Show job status |
| `help` | Show available commands |

Job completion notifications (success or failure) are posted to the configured notification channel.

## Access control

By default any workspace member can run commands. To restrict access, set **Allowed Slack user IDs** in the plugin configuration to a comma-separated list of Slack user IDs (e.g. `U0123ABC, U0456DEF`). You can find a user's ID in their Slack profile under *… → Copy member ID*.

## Configuration storage

Plugin configuration (including tokens) is stored in `TENDRIL_HOME/plugin-config/Ivy.Tendril.Plugin.Slack.json`. Delete this file to reset the plugin.
