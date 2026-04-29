---
searchHints:
  - overview
  - what-is
  - tendril
  - agent
  - orchestration
  - architecture
icon: Rocket
---

# Welcome to Ivy Tendril

<Ingress>
Tendril is an open source, local-first desktop application that serves as the operating system for AI-powered software development — orchestrating coding agents like Claude Code, Codex, Gemini, and Copilot through a structured lifecycle from idea to merged pull request.
</Ingress>

<Embed Url="https://youtu.be/X-zkkI8ah-E"/>

## The Concept

In Tendril, work is organized into **Plans**—structured units of work. Instead of a "black box" that outputs code and hopes for the best, Tendril moves your Plan through a defined lifecycle using Promptwares: isolated, single-purpose agents that specialize in specific stages. Whether it’s generating code, verifying builds, or opening PRs, you have total visibility. Tendril doesn't just autocomplete your lines; it orchestrates your workflow.

## Key Features

- **Plan Lifecycle** — From draft through execution, review, and PR — every step traceable, every decision logged.
- **Multi-Project** — Manage multiple repos with per-project verification rules and agent configuration.
- **Jobs Dashboard** — Track every running agent: status, token usage, cost, and execution history at a glance.
- **Promptwares** — Modular, self-improving agents (CreatePlan, ExecutePlan, ExpandPlan, CreatePr) that learn your codebase over time.
- **Git Worktrees** — Each agent works in an isolated worktree — your main branch stays clean until you approve.
- **Terminal & File Viewer** — Embedded terminal and fast local file access without leaving the app.
- **Verification Gates** — Wire up your build, test, lint, and format checks — code doesn't advance until it passes.
- **Stack & Agent Agnostic** — Works with any language, framework, or CLI-based agent — Claude Code, Codex, Gemini, and more. No lock-in.
- **Cross-Platform & Local-First** — Runs on Windows, macOS, and Linux. Your code never leaves your machine.

## The Tendril Loop: From Idea to PR

Plans flow through the following pipeline:

![Plan flow](assets/PlanLifecycle.png)

## Why Tendril?

At [Ivy Interactive](https://ivy.app), we experimented with many different systems of architecture in order to improve our workflow and take advantage of the advancements in AI/agentic coding capabilities. Working with the incredible capabilities of Claude and others was great, but it quickly became messy managing a dozen terminal windows.

Therefore, we created this system to streamline the experience of working with different agents. Through the [Promptware](../Concepts/Promptwares) architecture, we have created a feedback loop that ensures agents are not only organized and structured, but also self-improving according to the needs and context of the projects they work with. By centering the entire process on a **Plan**, you maintain the "Source of Truth" while specialized agents handle the heavy lifting.

<Callout type="tip">
We LOVE hearing from you! You are always welcome to report issues, bugs, and suggestions on our **[GitHub repository](https://github.com/Ivy-Interactive/Ivy-Tendril)**.  If you need direct help or would like to connect with the community, please join us on **[Discord](https://discord.gg/FHgxkDga3y)** — we'd love to see you there!
</Callout>