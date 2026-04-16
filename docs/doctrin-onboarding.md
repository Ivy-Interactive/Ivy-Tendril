# Ivy Tendril ❤️ Doctrin 

## About Doctrin

Doctrin operates a communication platform and a medical forms system serving both specialist and primary care in Sweden. The development team consists of 16 developers and one product owner, working in agile sprints with a well-established tool-chain: Jira for traceability from requirement to code, GitHub for version control with PR workflows, Confluence for documentation, and CI/CD via Flux deploying to Kubernetes on AWS.

The tech stack is .NET and JavaScript/TypeScript. All work is split between **Mission** (planned development and features) and **Launchpad** (bugs, smaller requests, support). LLM usage is permitted but requires a risk assessment, reflecting the compliance requirements inherent to healthcare software.

Doctrin has identified a clear ambition: **an agentic coding orchestration process that generates code based on their documentation, architecture, and processes** - one that follows their standards (Jira flows, test requirements, code conventions, Confluence references) and delivers measurable improvement over time in quality, accuracy, and reduction of manual correction.

Overall goal is to "have the cookie and eat it too" - increase developer productivity while improving quality and developer happiness. 

## About Ivy

At Ivy, we believe that humans bring ideas, taste, and experience - and that together with AI, great outcomes can be achieved. Our sole purpose is to minimize the friction when humans communicate with agents. Tendril is the product of that mission: a structured orchestration layer that lets developers stay in control while AI does the heavy lifting.

We also believe that companies are at an inflection point. Those who adopt coding orchestration as a core technique can realistically 10x their development output - not as a marketing claim, but as a practical outcome of letting AI handle the mechanical work while humans focus on architecture, design decisions, and quality.

Ivy Tendril is an AI orchestration platform that transforms AI-assisted coding from ad-hoc sessions into a structured, verifiable production workflow.

At its core, Tendril manages **Plans** - units of work that move through a defined lifecycle from planning to execution, verification, and PR creation. Each stage is handled by specialized agents (called **Promptwares**) with their own system prompts, tool permissions, and persistent memory. These are self-learning units that overtime becomes better and better at understaning your codebase. 

- **Orchestrated agent pipeline** - Specialized agents for planning, execution, code review, and PR creation, each scoped to do one thing well
- **Verification gates** - Automated build, test, lint, and format checks that must pass before code advances. Failures feed back to the agent with full logs for iterative correction
- **Isolated execution** - All agent work happens in git worktrees, completely separated from main branches until a human approves
- **Agent memory & learning** - Agents maintain persistent memory across runs, capturing patterns, conventions, and lessons specific to your codebase. They get better over time
- **Cost & token tracking** - Live monitoring of token usage and estimated spend per task, with full cost logs
- **Workflow integration** - Native GitHub integration for PRs and issues, with extensible inputs for ticket systems and documentation sources

Tendril is built on .NET and runs against any GitHub-hosted codebase. It supports multiple AI providers (Claude, Codex, Gemini) and can be configured per project with different verification rules, agent profiles, and execution depth.

## Proposal

We propose a **3 day on-site engagement** 27-29 April with two Ivy engineers to implement Ivy Tendril at Doctrin, structured around three workstreams that map directly to Doctrin's stated goals.

- Niels Bosma (Founder) https://www.linkedin.com/in/bosmaniels/
- Mikael Rinne (Founding engineer) https://www.linkedin.com/in/mikael-rinne/

Doctrin select a small implementation team working together with team Ivy during these days to rollout Tendril in the organisation.

Team Ivy will prepare Tendril in advance with the following additions:

- Jira integration
- Confluence integration 

### Day 1: Architecture & Foundation

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

### Day 2: Training & Evaluation

**Goal:** A feedback loop that makes the system measurably better over time.

- Onboard remaining developers
- Define "good output" criteria together with the team - what does a successful AI-generated PR look like at Doctrin?
- Set up evaluation loops: automated verification results, human review feedback, and agent memory that captures learnings across runs
- Implement the self-improving loop: agent executes, verification gates check results, failures feed back with logs, agent iterates, and successful patterns are captured in memory for future runs
- Establish metrics: PR acceptance rate, verification pass rate, human correction frequency, cost per plan

**Deliverable:** A functioning self-improvement loop with defined metrics and agent memory that accumulates project-specific knowledge.

### Day 3: Stabilization, Culture & Handoff

**Goal:** A self-sufficient team with a stable system and the mindset to get the most out of it.

- Triage and fix any issues surfaced during Day 2 usage — broken verification gates, agent misconfiguration, missing context, prompt refinements
- Participate in a culture workshop with the team:
    - **This workshop should be driven by Doctrin engineering leadership with Ivy participation. Think of this as a retrospective.**
    - How should we think about AI-assisted development: when to delegate vs. when to code yourself
    - Writing effective plans and tickets that agents can execute well
    - Is the current "life as a developer at Doctrin" still the best? What cultural changes should we make
    - Review real outputs from Day 2 together: what worked, what didn't, and why
- Handoff:
    - Document the configuration: agent profiles, prompt architecture, verification pipelines, memory structures
    - Transfer ownership of Tendril administration — who maintains configs, reviews agent memory, tunes prompts
    - Define escalation patterns: what the agents handle autonomously, what requires human review, what they shouldn't attempt
    - Leave a roadmap: recommended next steps for expanding to more repos, adding custom promptwares, deepening Jira/Confluence integration

**Deliverable:** A stable, documented Tendril installation owned by the Doctrin team, with developers trained to operate, tune, and expand the system independently.

## Quote

200.000:- SEK (excl. VAT) one-off

The price covers:

- Pre-engagement preparation (environment review, configuration planning, integration implementation)
- A version of Ivy Tendril optimized for Doctrin with a GitHub-backed configuration that can be shared between all developers
- 3 days on-site implementation and coaching
- Post-engagement support: 2 weeks of remote availability for questions and troubleshooting in a dedicated Slack channel
- **A productive and happy team!**

### Estimated Running Costs

Tendril's ongoing cost is primarily LLM API usage. Based on typical workloads:

| Task type | Estimated cost per plan | Notes |
|---|---|---|
| Bug fix / small change | $1–3 | Single execution + verification loop |
| Feature (medium) | $3–10 | Planning + execution + review + iteration |
| Feature (large / multi-file) | $10–25 | Multiple agent passes, deeper context |

For a team of 16 developers running ~20 plans per day, expect **$800–2,500/month** in LLM costs depending on task complexity and iteration depth. This scales linearly with usage — there are no per-seat or platform fees for Tendril itself.

Each plan tracks token usage and cost in real-time, giving full visibility into spend.
