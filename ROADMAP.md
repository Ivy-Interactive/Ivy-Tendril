# Ivy Tendril Roadmap

This document outlines the core feature roadmap for **Ivy Tendril**, the AI coding agent orchestrator. It details each planned initiative with a short description and the exact capabilities Tendril and its AI agent ecosystem provide.

---

## Roadmap Features & Epics

### 1. Kanban
* **Description**: Interactive visual board for managing plans and agent jobs across lifecycle states (Icebox, Drafts, In Progress, Review, Completed, Trash).
* **What Tendril Can Do**: Provide real-time web UI status cards with live token and cost tickers, drag-and-drop plan state transitions, agent execution controls, and customizable swimlanes per team workflow.

### 2. Code Review
* **Description**: Automated and interactive diff verification process before merging agent-generated code into target branches.
* **What Tendril Can Do**: Analyze multi-file git diffs, highlight potential breaking changes or regression risks, run automated verification test suites, present interactive review UI for human approval, and auto-generate PR descriptions.

### 3. Custom Workflows
* **Description**: User-configurable execution pipelines, hooks, and verification gates tailored to specific project requirements.
* **What Tendril Can Do**: Support custom YAML and JSON workflow definitions, trigger multi-agent execution sequences (e.g., plan to code to test to review), define custom git workflow hooks, and enforce conditional branch gates.

### 4. Memory / Library
* **Description**: Long-term project context retention, architectural memory vault integration, and reusable code pattern repository.
* **What Tendril Can Do**: Integrate seamlessly with `.brainwares` and Promptwares vaults to retain architectural context, index repository symbols, query historical plan resolutions, and inject relevant context into active agent prompts.

### 5. Cost Control
* **Description**: Token usage monitoring, budget caps, cost forecasting, and model expense analytics across agents and projects.
* **What Tendril Can Do**: Track real-time token spend per job, enforce budget thresholds per plan or user, automatically route tasks to cost-effective models (e.g., Flash vs Pro), and trigger alerts when spending thresholds are reached.

### 6. Integrations-Connections
* **Description**: ChatOps notification bot and conversational interface for triggering, monitoring, and reviewing agent jobs via Slack.
* **What Tendril Can Do**: Post real-time plan status updates to Slack channels, send interactive approval cards for PR reviews, allow team members to create plans via Slack slash commands, and broadcast job completion alerts.

### 7. Bring Your Own LLM / Local Model
* **Description**: Flexible model orchestration supporting local open-source models (Ollama, vLLM, LM Studio) and custom API endpoints.
* **What Tendril Can Do**: Configure custom API base URLs, support local LLMs for privacy-sensitive offline environments, handle model routing based on task complexity, and balance local vs cloud model invocation.
