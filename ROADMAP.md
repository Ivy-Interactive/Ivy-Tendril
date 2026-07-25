# Ivy Tendril Roadmap

This document outlines the core feature roadmap for **Ivy Tendril**, the AI coding agent orchestrator. It details each planned initiative with a short description and the exact capabilities Tendril and its AI agent ecosystem provide.

---

## Roadmap Features & Epics

### 1. Core Epics
* **Description**: High-level feature groupings and architectural milestones that break down major product initiatives into structured, multi-stage development plans.
* **What Tendril Can Do**: Automatically parse product requirements into structured Epics, decompose them into executable child plans, track progress across multi-agent pipelines, and maintain state synchronization across repository boundaries.

### 2. Kanban
* **Description**: Interactive visual board for managing plans and agent jobs across lifecycle states (Icebox, Drafts, In Progress, Review, Completed, Trash).
* **What Tendril Can Do**: Provide real-time web UI status cards with live token and cost tickers, drag-and-drop plan state transitions, agent execution controls, and customizable swimlanes per team workflow.

### 3. Code Review
* **Description**: Automated and interactive diff verification process before merging agent-generated code into target branches.
* **What Tendril Can Do**: Analyze multi-file git diffs, highlight potential breaking changes or regression risks, run automated verification test suites, present interactive review UI for human approval, and auto-generate PR descriptions.

### 4. UX Mockup
* **Description**: Visual UI layout prototyping and interactive wireframe generation integrated directly into plan lifecycle drafting.
* **What Tendril Can Do**: Generate visual UI mockups, render interactive component previews, convert visual mockups into Ivy UI or React components, and let stakeholders inspect UI options prior to full code execution.

### 5. Custom Workflows
* **Description**: User-configurable execution pipelines, hooks, and verification gates tailored to specific project requirements.
* **What Tendril Can Do**: Support custom YAML and JSON workflow definitions, trigger multi-agent execution sequences (e.g., plan to code to test to review), define custom git workflow hooks, and enforce conditional branch gates.

### 6. Memory / Library
* **Description**: Long-term project context retention, architectural memory vault integration, and reusable code pattern repository.
* **What Tendril Can Do**: Integrate seamlessly with `.brainwares` and Promptwares vaults to retain architectural context, index repository symbols, query historical plan resolutions, and inject relevant context into active agent prompts.

### 7. Cost Control
* **Description**: Token usage monitoring, budget caps, cost forecasting, and model expense analytics across agents and projects.
* **What Tendril Can Do**: Track real-time token spend per job, enforce budget thresholds per plan or user, automatically route tasks to cost-effective models (e.g., Flash vs Pro), and trigger alerts when spending thresholds are reached.

### 8. Background Service
* **Description**: Continuous daemon background service for autonomous plan execution, inbox watching, and async queue processing.
* **What Tendril Can Do**: Run Tendril as a background system service (macOS launchd, systemd, Windows Service), handle incoming webhooks and file watcher triggers, execute master election in multi-node setups, and run jobs headlessly.

### 9. Plugins
* **Description**: Modular extension architecture for third-party tools, custom agent capabilities, and external service integrations.
* **What Tendril Can Do**: Support loading custom plugin packages, register new MCP and CLI tool definitions, extend the Ivy web UI with custom widgets, and hook into agent lifecycle events.

### 10. Slack Integration
* **Description**: ChatOps notification bot and conversational interface for triggering, monitoring, and reviewing agent jobs via Slack.
* **What Tendril Can Do**: Post real-time plan status updates to Slack channels, send interactive approval cards for PR reviews, allow team members to create plans via Slack slash commands, and broadcast job completion alerts.

### 11. Question (or Questions)
* **Description**: Interactive clarification dialogs allowing agents to prompt developers when plan requirements contain ambiguity.
* **What Tendril Can Do**: Present structured multiple-choice or write-in clarification questions in the web app and CLI, block execution safety gates until answered, and fold user responses directly into prompt context.

### 12. New Projects Templates
* **Description**: Standardized project starter templates and scaffolding wizards for rapidly initializing new codebases and Ivy apps.
* **What Tendril Can Do**: Scaffold new projects from pre-configured templates (Web, API, CLI, Microservices), inject recommended agent instructions and verification scripts, and auto-configure git repositories with Tendril workflows.

### 13. Dependencies
* **Description**: Automated package dependency management, security vulnerability auditing, and version upgrade tracking.
* **What Tendril Can Do**: Scan project manifests (NuGet, npm, pip), detect outdated or vulnerable packages, formulate dependency upgrade plans, and execute test suites to verify backwards compatibility.

### 14. Bring Your Own LLM / Local Model
* **Description**: Flexible model orchestration supporting local open-source models (Ollama, vLLM, LM Studio) and custom API endpoints.
* **What Tendril Can Do**: Configure custom API base URLs, support local LLMs for privacy-sensitive offline environments, handle model routing based on task complexity, and balance local vs cloud model invocation.

### 15. Agents & Additional Capabilities
* **Description**: Modular subagent framework equipping primary agents with specialized domain tools and capabilities.
* **What Tendril Can Do**: Spawn specialized subagents (e.g., refactoring agent, test authoring agent, documentation agent), orchestrate multi-agent collaboration, and dynamically assign tool privileges based on role.

### 16. Built-in Webpage for Instant UX Feedback
* **Description**: Embedded live application preview page enabling instant visual and interactive feedback on generated UI code.
* **What Tendril Can Do**: Host an in-app live preview page, capture visual UI feedback and user annotations, auto-trigger live reload on code edits, and pass visual feedback directly into agent iteration cycles.

### 17. Code Review Agent
* **Description**: Specialized AI agent dedicated to automated pull request reviews, code quality enforcement, and style adherence.
* **What Tendril Can Do**: Perform automated static analysis, verify compliance with repository rules (e.g., AGENTS.md), identify anti-patterns, comment directly on code diff lines, and approve or request changes on pull requests.

### 18. Security Review Agent
* **Description**: Autonomous security auditing agent focused on vulnerability detection, secret scanning, and compliance checks.
* **What Tendril Can Do**: Scan code for OWASP Top 10 vulnerabilities, detect hardcoded secrets or API keys, analyze dependency supply chain risks, and generate actionable remediation plans for security findings.

### 19. Epic Planning
* **Description**: Strategic planning tool for decomposing large product initiatives into multi-stage execution roadmaps.
* **What Tendril Can Do**: Convert natural language product specifications into multi-plan Epics, map out plan dependency graphs, estimate complexity and token costs, and track macro progress toward initiative completion.

### 20. Team Collab
* **Description**: Multi-user collaboration features, shared workspace state, agent assignment, and audit logging.
* **What Tendril Can Do**: Support user roles and permissions, synchronized real-time dashboard updates, team comments on plans, shared agent prompt libraries, and comprehensive activity audit trails.

### 21. Ivy Tendril Agent
* **Description**: Native self-hosting Meta-Agent capable of analyzing, modifying, and upgrading the Ivy Tendril codebase itself.
* **What Tendril Can Do**: Autonomously repair Tendril bugs, implement requested Tendril extensions, update Tendril documentation, and run Tendril's test suite for self-directed enhancement.

### 22. Cloud Dev Hosting
* **Description**: Remote development workspace hosting, containerized execution environments, and cloud agent orchestration.
* **What Tendril Can Do**: Spin up ephemeral dev containers in the cloud (Docker, Azure, AWS), run agent jobs in isolated environments, host remote Tendril instances accessible via web, and manage cloud execution resources.
