# Ivy Tendril Roadmap

This document outlines the core feature roadmap for **Ivy Tendril**, the AI coding agent orchestrator. It details each planned initiative with its description, purpose, implementation details, associated risks, and estimated timelines.

---

## Roadmap Features & Epics

### 1. Refined Jobs App
- **Description**: Interactive visual Kanban board for managing plans and agent jobs across lifecycle states (Icebox, Drafts, In Progress, Review, Completed, Trash).
- **Purpose**: Provide a clear, human-friendly visual representation of agent execution states and streamline job management workflows.
- **Implementation Details**:
  - Build Kanban board UI components supporting drag-and-drop state transitions and filtering.
  - Implement real-time status synchronization between the agent execution runtime and the frontend.
  - Add lifecycle state management (Icebox, Drafts, In Progress, Review, Completed, Trash) with action handlers.
- **So far done**: Figma design
- **Risks**:
  - Low: None
  - Medium: UI state concurrency issues with rapid asynchronous agent updates; potential performance degradation when loading high volumes of historical jobs.
  - High: None
- **Timelines**: 40-80 hours for MVP, 160 hours for production ready v1

---

### 2. Refined Git Diff App
- **Description**: Automated and interactive diff verification process for inspecting and approving code before merging agent-generated changes into target branches with UI similar to GitHub
- **Purpose**: Ensure safe, reliable, and human-reviewed code integration, preventing breaking changes or unwanted diffs from being merged into target branches.
- **Implementation Details**:
  - Integrate unified and side-by-side syntax-highlighted diff viewers into Tendril.
  - Implement file edit (with LSP), file delete, agentic edit and delete and create new commits
  - Implement run agentic review on your machine, and proccess the submitted human- and agentic review
- **So far done**: 90% MVP
- **Risks**:
  - Low: Performance degradation when loading high volumes of historical jobs. Integration the tendril jobs flow for running agentic reviews
  - Medium: None    
  - High: None
- **Timelines**: 40-80 hours for MVP, 160 hours for production ready v1

---

### 3. Custom Workflows
- **Description**: User-configurable execution pipelines, hooks, and verification gates tailored to specific project standards and requirements. Read-only functionality similar to n8n. View tendril flow for each project, and create custom versions of it with fine-grained models defined.
- **Purpose**: Empower development teams to define custom multi-agent execution steps, enforce repository quality gates, and automate complex developer workflows.
- **Implementation Details**:
  - Canvas with nodes for viewing existing workflows with drag-and-drop functionality
  - Agent to do modifications
  - Integration with Connections
  - Export workflow as SVG to be used by other agents in reporting of the current state of each workflow
- **So far done**: 10% MVP, figma design
- **Risks**:
  - Low: None
  - Medium: choose best UX for modifications of flows
  - High: potential issues when integration with the Chat, Jobs and Connections apps
- **Timelines**: 60-120 hours for MVP, 300 hours for production ready v1

---

### 4. Memory / Library
- **Description**: Long-term project context retention, architectural memory vault integration, and reusable code pattern repository for improved tokenomics and speed
- **Purpose**: Maintain architectural consistency across coding sessions, retain historical plan outcomes, and eliminate redundant context gathering by agents.
- **Implementation Details**:
  - Build memory vaults for viewing memories per tendril, per project and per promptware
  - Build mechanism to validate and invalidate, prune and create new memories
  - Build visual canvas and file browser for memories.
  - Define agents for automatic memory management
- **So far done**: 50% MVP
- **Risks**:
  - Low: tricky to create an innovative canvas
  - Medium: defining nice UX for running automated memory-related jobs
  - High: potential issues in memory creation while running jobs, getting too many/few / wrong memories
- **Timelines**: 80-120 hours for MVP, 250 hours for production ready v1

---

### 5. Cost Control
- **Description**: Real-time token usage monitoring, budget caps, cost forecasting, and model expense analytics across agents and projects with suggested actions
- **Purpose**: Prevent unexpected API cost overruns, optimize model selection according to task difficulty, and provide transparency into token expenditure.
- **Implementation Details**:
  - Implement per-job, per-plan, and per-user token and cost tracking.
  - Add configurable soft and hard budget caps with automated task pause/kill options on breach.
  - Create a dynamic model routing engine (e.g., routing routine subtasks to lightweight models like Gemini Flash and complex tasks to Pro models).
  - Build cost analytics dashboards and real-time alert notifications.
- **So far done**: Reporting feature by Renco
- **Risks**:
  - Low: None
  - Medium: None
  - High: creating false reports due to incorrect data and taking incorrect actions
- **Timelines**: 60-120 hours for MVP, 200 hours for production ready v1

---

### 6. Connections
- **Description**: Robust Connections system with predefined list of connections, that allows you to connect tendril with an existing MCP or API like Slack, Jira, GitHub, etc
- **Purpose**: Bring agent orchestration into daily team communication channels, web/desktop apps, mobile apps, accelerating review cycles and making agent management accessible to all team members.
- **Implementation Details**:
  - Make connections manageable with CRUD operations, agents for creating and deleting connections
  - Make connections installable as plugins
  - Added connection allows tendril's agents to do actions with it
  - Added connection can be used as input/output/trigger in the workflows app
- **So far done**: Figma design, 1% MVP 
- **Risks**:
  - Low: 
  - Medium: 
  - High: 
- **Timelines**: 60-120 hours for MVP, 200 hours for production ready v1

---

### 7. Bring Your Own LLM / Local Model
- **Description**: Flexible model orchestration supporting local open-source models (Ollama, vLLM, LM Studio) and custom API endpoints.
- **Purpose**: Enable secure, privacy-focused, offline-capable agent execution while allowing teams to use custom or self-hosted LLM endpoints.
- **Implementation Details**:
  - Develop provider-agnostic API clients supporting OpenAI-compatible REST schemas and local backends (Ollama, vLLM, LM Studio).
  - Build a custom endpoint configuration UI within Tendril settings.
- **So far done**: 90% MVP with ivy-agent using 3 provided models + custom agentic TUI that works with any endpoint
- **Risks**:
  - Low: 
  - Medium: 
  - High: 
- **Timelines**: 60-120 hours for MVP, 200 hours for production ready v1
