# Ivy Tendril - Implementation Engagement for Doctrin

## Background

Doctrin operates a communication platform and a medical forms system serving both specialist and primary care in Sweden. The development team consists of 16 developers and one product owner, working in agile sprints with a well-established toolchain: Jira for traceability from requirement to code, GitHub for version control with PR workflows, Confluence for documentation, and CI/CD via Flux deploying to Kubernetes on AWS.

The tech stack is .NET and JavaScript/TypeScript. All work is split between **Mission** (planned development and features) and **Launchpad** (bugs, smaller requests, support). LLM usage is permitted but requires a risk assessment, reflecting the compliance requirements inherent to healthcare software.

Doctrin has identified a clear ambition: **an AI agent that generates code based on their documentation, architecture, and processes** - one that follows their standards (Jira flows, test requirements, code conventions, Confluence references) and delivers measurable improvement over time in quality, accuracy, and reduction of manual correction.

The overall goal is to have the cookie and eat it too - **increase developer productivity while simultaneously improving code quality and developer happiness.**

### About Ivy

At Ivy, we believe that humans bring ideas, taste, and experience - and that together with AI, great outcomes can be achieved. Our sole purpose is to minimize the friction when humans communicate with agents. Tendril is the product of that mission: a structured orchestration layer that lets developers stay in control while AI does the heavy lifting.

We also believe that companies are at an inflection point. Those who adopt coding orchestration as a core technique can realistically 10x their development output - not as a marketing claim, but as a practical outcome of letting AI handle the mechanical work while humans focus on architecture, design decisions, and quality.

### About Ivy Tendril

Ivy Tendril is an AI orchestration platform that transforms AI-assisted coding from ad-hoc autocomplete into a structured, verifiable production workflow.

At its core, Tendril manages **Plans** - units of work that move through a defined lifecycle from planning to execution, verification, and PR creation. Each stage is handled by specialized agents (called **Promptwares**) with their own system prompts, tool permissions, and persistent memory. Unlike black-box AI tools, every step is visible, traceable, and auditable.

Key capabilities:

- **Orchestrated agent pipeline** - Specialized agents for planning, execution, code review, and PR creation, each scoped to do one thing well
- **Verification gates** - Automated build, test, lint, and format checks that must pass before code advances. Failures feed back to the agent with full logs for iterative correction
- **Isolated execution** - All agent work happens in git worktrees, completely separated from main branches until a human approves
- **Agent memory & learning** - Agents maintain persistent memory across runs, capturing patterns, conventions, and lessons specific to your codebase. They get better over time
- **Prompt versioning** - Each agent's system prompt and configuration is versioned and tunable, giving full control over agent behavior
- **Cost & token tracking** - Live monitoring of token usage and estimated spend per task, with full cost logs
- **Workflow integration** - Native GitHub integration for PRs and issues, with extensible inputs for ticket systems and documentation sources

Tendril is built on .NET and runs against any GitHub-hosted codebase. It supports multiple AI providers (Claude, Codex, Gemini) and can be configured per project with different verification rules, agent profiles, and execution depth.

## Proposal

We propose a **one-week on-site engagement** with two Ivy engineers to implement Ivy Tendril at Doctrin, structured around three workstreams that map directly to Doctrin's stated goals.

### Week Structure

#### Days 1-2: Architecture & Foundation

**Goal:** A working Tendril installation tailored to Doctrin's environment.

- Install and configure Tendril against Doctrin's GitHub repositories
- Design the prompt architecture: system context, project-specific memory, and feedback structures aligned to Doctrin's codebase and conventions
- Map Doctrin's development workflow into Tendril's orchestration model:
  - Jira tickets as plan inputs
  - Confluence documentation as agent context
  - PR creation linked back to Jira
- Configure agent profiles (execution depth, tool permissions, verification gates) appropriate for Doctrin's .NET and TypeScript projects
- Set up verification pipelines (build, test, lint, format) matching Doctrin's existing CI expectations

**Deliverable:** A running Tendril instance integrated with Doctrin's GitHub repos, with agents configured to understand their codebase, conventions, and workflow.

#### Days 3-4: Self-Improvement & Evaluation

**Goal:** A feedback loop that makes the system measurably better over time.

- Define "good output" criteria together with the team - what does a successful AI-generated PR look like at Doctrin?
- Set up evaluation loops: automated verification results, human review feedback, and agent memory that captures learnings across runs
- Configure prompt versioning through Tendril's promptware system - each agent maintains versioned system prompts and persistent memory files that evolve based on outcomes
- Implement the self-improving loop: agent executes, verification gates check results, failures feed back with logs, agent iterates, and successful patterns are captured in memory for future runs
- Establish metrics: PR acceptance rate, verification pass rate, human correction frequency, cost per plan

**Deliverable:** A functioning self-improvement loop with defined metrics and agent memory that accumulates project-specific knowledge.

#### Day 5: Healthcare Compliance & Team Enablement

**Goal:** A solution that works within Doctrin's compliant environment, and a team that can run it independently.

- Map Doctrin's critical flows to appropriate agent configurations with guardrails:
  - Isolated execution via git worktrees (agent code never touches main branches directly)
  - Human-in-the-loop review gates before any PR is created
  - Verification gates that enforce test coverage and build integrity
  - Configurable tool permissions per agent to limit scope of operations
- Define risk boundaries: what agents should and should not do in a healthcare context
- Conduct hands-on coaching sessions with the development team:
  - How to create and manage plans
  - How to review and provide feedback on agent output
  - How to tune prompts, adjust agent memory, and extend the system
  - How to monitor costs, track quality metrics, and troubleshoot
- Document Doctrin-specific runbooks and configuration decisions

**Deliverable:** A compliant, guardrailed setup with a team that understands how to operate, improve, and extend the system independently.

### Working Model

**"You build, we guide."** Doctrin's developers will be hands-on throughout the engagement. Our engineers provide architecture decisions, configuration expertise, and coaching - the goal is transfer of knowledge, not dependency. By the end of the week, the team should be self-sufficient.

### What Doctrin Gets

| Capability | Detail |
|---|---|
| **Structured AI orchestration** | Plans move through a defined lifecycle with full traceability |
| **Multi-agent architecture** | Specialized agents for planning, execution, review, and PR creation |
| **Prompt versioning & memory** | Agents learn from each run and improve over time |
| **Verification gates** | Build, test, lint, and format checks before code advances |
| **Isolated execution** | Git worktrees keep agent work separate until human-approved |
| **Workflow integration** | GitHub PRs, Jira traceability, Confluence as context |
| **Cost visibility** | Token usage, estimated spend, and cost tracking per task |
| **Measurable improvement** | Defined metrics to track quality and accuracy over time |

## Quote

| Item | Detail | Price |
|---|---|---|
| On-site implementation | 1 week, 2 engineers | SEK 200,000 |
| Travel & accommodation | As needed | At cost |

**Total: SEK 200,000 + travel expenses**

The price covers:
- Pre-engagement preparation (environment review, configuration planning)
- 5 days on-site implementation and coaching
- Tendril software license for the engagement period
- Post-engagement support: 2 weeks of remote availability for questions and troubleshooting

**Tendril licensing** beyond the engagement period will be quoted separately based on team size and usage volume.

---

*Ivy Interactive AB*
*Date: April 16, 2026*
