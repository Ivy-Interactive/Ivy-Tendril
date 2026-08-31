# Telemetry Data Classification Policy

## Purpose

This document defines what data Tendril may and may not send to third-party telemetry services (PostHog). The goal is to collect useful analytics while respecting user privacy.

Telemetry is opt-out: it is on by default and disabled by setting `telemetry: false` in the settings section of `config.yaml` (see `Settings.Telemetry` in [ConfigService.cs](Services/ConfigService.cs)). When disabled, no PostHog client is constructed and every `Track*` call is a no-op.

Users are identified only by a random GUID persisted to `<LocalAppData>/Tendril/.anonymous-id` (falling back to `TENDRIL_HOME` or the temp directory in Docker). It is never derived from a username, machine name, or repository.

## Classification Rules

### ALLOWED - Aggregate & Non-Identifying Data

**Safe to track:**
- **Counts**: Number of projects, repos, plans, jobs (aggregate totals only)
- **Durations**: Time taken to complete operations (in seconds)
- **States/Types**: Enum values, state names, job types (e.g., "CreatePlan", "ExecutePlan")
- **Levels**: Plan levels (e.g., "Bug", "Feature", "Epic")
- **Versions**: Application version strings, OS platform and version strings
- **Agent providers**: Coding agent name (e.g., "claude", "codex", "copilot", "gemini")
- **Booleans**: Feature flags, configuration states (e.g., llm_configured: true)
- **Status codes**: Success/failure indicators, verification results
- **Technology descriptors**: The project stack hash (see below)
- **Install-salted one-way hashes** of otherwise-forbidden identifiers (see below)

**Why these are safe:**
- They cannot identify individual users, repositories, or organizations
- They provide useful aggregate analytics (usage patterns, performance metrics)
- They respect the principle of data minimization

#### Stack hash (`stack_hash`)

The Stack Descriptor Hash is a canonical, similarity-preserving signature of a project's tech stack, e.g. `fe.ts:react+next+tailwind/be.py:fastapi/db:postgres/test:pytest`. It is composed only from a closed vocabulary of language, framework, database, and test-framework slugs — by construction it carries no names, paths, versions, counts, or free text. It tells us which stacks Tendril is used on without revealing whose project it is. The grammar and derivation rules live in [SetupProject/Program.md](Promptwares/SetupProject/Program.md).

#### Install-salted plan identity (`plan_uuid`)

Raw plan ids remain forbidden (see below), but events still need to be groupable per plan. `TelemetryService.DerivePlanUuid` therefore emits `SHA256("tendril-plan:" + anonymousId + ":" + planId)`, formatted as an RFC 9562 v8 UUID, instead of the id itself. This is acceptable because:

- The anonymous id acts as a per-install salt, so plan `00042` derives a different value on every install and cannot be used to correlate unrelated users
- The hash is one-way, so the sequential counter never leaves the machine
- It is scoped to a single anonymous user, so it groups events without widening identity

Any future need to correlate a forbidden identifier must use this same salted-hash pattern, never the raw value.

### FORBIDDEN - Identifying Information

**Never track:**
- **URLs**: Repository URLs, PR URLs, issue URLs
- **Paths**: File paths, directory paths, absolute paths to repos
- **Usernames**: GitHub usernames, organization names, email addresses
- **Repository names**: Specific repository identifiers
- **Project names**: Even generic names from config.yaml (e.g., "Tendril", "Framework") reveal work context
- **Sequential IDs**: Plan IDs, issue numbers, PR numbers that could correlate users (hash them per install instead — see `plan_uuid`)
- **User input**: Task descriptions, commit messages, plan content
- **Titles**: Plan titles, issue titles, commit subjects
- **Agent output**: Agent transcripts, tool calls, or error messages that may embed user content

**Why these are forbidden:**
- They can reveal private repository information
- They may expose personal or organizational identifiers
- Project names reveal what the user is working on, which could be sensitive business information
- Sequential IDs can be used to correlate activity across anonymous users
- User-provided content may contain sensitive or proprietary information

## Decision Framework

When adding new telemetry events, ask:

1. **Can this field identify a person or organization?** -> Forbidden
2. **Can this field reveal private repository information?** -> Forbidden
3. **Can this field reveal what the user is working on?** -> Forbidden
4. **Can this field be correlated across users to de-anonymize them?** -> Forbidden, unless salted with the anonymous id and hashed one-way
5. **Does this field provide useful aggregate insights?** -> Allowed

**When in doubt, leave it out.**

## Attached To Every Event

Set as PostHog super properties in the `TelemetryService` constructor:

| Property | Status | Notes |
|----------|--------|-------|
| `$session_id` | Compliant | Random GUID, new per process |
| `$geoip_disable: false` | Accepted | PostHog resolves the request IP to a country so we can see where Tendril is used; the IP is not stored as an event property |
| `app_version` | Compliant | Assembly version, 3 parts |
| `os` | Compliant | Platform enum name |
| `os_version` | Compliant | OS version string |

`IdentifyAsync` additionally sets `app_version`, `os`, `os_version` as person properties, and `first_seen` (UTC timestamp) once.

## Current Events Audit

All events comply with this policy:

| Event | Properties | Status | Emitted from |
|-------|------------|--------|--------------|
| `app_started` | version, project_count, llm_configured | Compliant | [TendrilServer.cs](TendrilServer.cs) |
| `onboarding_completed` | project_count, agent | Compliant | [OnboardingSetupService.cs](Services/OnboardingSetupService.cs) |
| `project_created` | repo_count, stack_hash | Compliant | Onboarding, Settings add-project, `tendril project` |
| `job_created` | job_type, agent, plan_uuid | Compliant | [JobService.cs](Services/Jobs/JobService.cs) |
| `job_completed` | job_type, status, duration_seconds, agent, plan_uuid | Compliant | [JobCompletionHandler.cs](Services/Jobs/JobCompletionHandler.cs) |
| `plan_created` | level, duration_seconds, agent, stack_hash, plan_uuid | Compliant | [JobCompletionHandler.cs](Services/Jobs/JobCompletionHandler.cs) |
| `pr_created` | duration_seconds, agent, plan_uuid | Compliant | [JobCompletionHandler.cs](Services/Jobs/JobCompletionHandler.cs) |
| `plan_state_transition` | from_state, to_state, plan_uuid | Compliant | [PlanReaderService.cs](Services/Plans/PlanReaderService.cs) |

`plan_uuid` is always the derived, install-salted value: call sites pass the raw plan id into the typed context and `TelemetryService` hashes it before capture, so the raw id cannot reach PostHog even if a new call site is unaware of the rule.

## Implementation

- [ITelemetryService.cs](Services/Telemetry/ITelemetryService.cs) — typed context objects that enforce this policy at compile time. New events get a context record here rather than a loose property bag.
- [TelemetryService.cs](Services/Telemetry/TelemetryService.cs) — PostHog client, anonymous id, plan uuid derivation. Every `Track*` method swallows its own exceptions: telemetry must never break the app.
- [TelemetryPlanUuidTests.cs](../Ivy.Tendril.Test/Services/TelemetryPlanUuidTests.cs) — plan uuid derivation and id normalization.
- [TelemetryServicePosthogTests.cs](../Ivy.Tendril.Test/TelemetryServicePosthogTests.cs) — manual, skipped by default; run explicitly to verify live ingestion.

## History

- 2026-08-24: Added `job_created`, `project_created`, `onboarding_completed` events; added install-salted `plan_uuid` grouping; fixed `stack_hash` on `plan_created`
- 2026-06-25: Added `stack_hash` to project and plan events alongside the project-analyzer command
- 2026-05-17: Added OS version, app_version as super properties; added agent provider to job/plan/pr events
- 2026-04-06: Plan 02069 removed repo_url from pr_created event
- 2026-04-06: Plan 02085 established this policy document and removed project names and plan IDs
