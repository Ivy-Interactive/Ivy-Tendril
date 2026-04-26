# Common Failure Modes

Patterns observed from past PlanEvaluator runs and debugging sessions.

## Instruction Gap Failures

### Silently Skipped Deliverables
- **Symptom**: Plan revision lists N components to create, agent only creates N-1
- **Root cause**: Program.md doesn't enforce a checklist-driven approach per deliverable
- **Where to look**: Compare revision acceptance criteria against actual file changes in commits
- **Fix area**: `Promptwares/ExecutePlan/Program.md` — add explicit deliverable tracking

### CheckResult False Positive
- **Symptom**: Verification marked Pass despite missing files or unmet targets
- **Root cause**: Verification prompt checks build/test success but doesn't cross-reference the full plan revision
- **Where to look**: `verification/*.md` reports, compare against revision requirements
- **Fix area**: Verification prompts in `config.yaml`, `Promptwares/ExecutePlan/Program.md` Step 7

## Token Waste Patterns

### Redundant File Reads
- **Symptom**: Same file read 4+ times in a single session
- **Where to look**: JSONL analysis — count Read tool calls per file path
- **Threshold**: >3 reads of the same file is suspicious, >6 is definitely wasteful

### Context Bloat
- **Symptom**: Single message with very high `input_tokens` (>50k)
- **Where to look**: JSONL messages sorted by input_tokens
- **Root cause**: Usually reading large files when grep would suffice

## Error Loop Patterns

### Build-Fix Cycles
- **Symptom**: 3+ iterations of build → error → edit → build
- **Where to look**: JSONL tool call sequences, grep for `dotnet build` or `npm run build`
- **Root cause**: Agent missing knowledge about API changes, type system, or framework patterns
- **Fix area**: `Promptwares/{Type}/Memory/` — add knowledge about the specific error pattern

### Type Mismatch Loops
- **Symptom**: Agent repeatedly tries different type casts/interfaces
- **Where to look**: JSONL edit tool calls on the same lines
- **Root cause**: Missing memory about the project's type hierarchy
- **Example**: IStream→IWriteStream confusion in Ivy framework

## Environmental Failures

### Stuck Plans
- **Symptom**: Plan stays in Building/Executing state indefinitely
- **Where to look**: `PlanReaderService.RecoverStuckPlans()` — runs on startup only
- **Root cause**: Agent process died without cleanup
- **Fix area**: `Services/PlanReaderService.cs`

### Plan Counter Collisions
- **Symptom**: Two plans with the same ID
- **Where to look**: `$TENDRIL_PLANS/.counter`
- **Root cause**: Multiple Tendril instances running simultaneously

### Missing Logs
- **Symptom**: No `.raw.jsonl` for a promptware execution
- **Where to look**: Check if `Logs/` directory exists under the promptware folder
- **Root cause**: `WriteRawOutputLog` in `JobCompletionHandler.cs` only writes if the directory exists

## CLI / Shim Failures

### Bash Shim Broken Quotes (Fixed 2026-04-26)
- **Symptom**: Every `tendril` CLI call fails with `unexpected EOF while looking for matching '"'`. The entire execution should fail since there is no fallback — the CLI is required.
- **Where to look**: Check JSONL for repeated bash errors mentioning the tendril command.
- **Root cause**: The bash shim at `/tmp/tendril-shim/tendril` was generated with Windows backslash paths in double quotes. A trailing `\"` was interpreted as an escaped quote by bash.
- **Fix area**: `Services/JobLauncher.cs` — shim generation. Bash shim now uses single quotes and forward slashes.

### CLI Process File Lock
- **Symptom**: `dotnet run --project` fails because another process holds a lock on the output directory
- **Where to look**: JSONL errors mentioning file locks, `MSB3027`, or `IOException` on `.dll` files
- **Root cause**: Multiple concurrent agent processes sharing the same shim build output directory
- **Fix area**: Each agent gets its own `BaseOutputPath` via the shim generation code

## Verification Integrity Failures

### Delegated Verification Self-Certification (Fixed 2026-04-26)
- **Symptom**: A verification that has its own dedicated promptware (e.g., `IvyFrameworkVerification`) is marked Pass by the ExecutePlan agent without actually running the separate promptware. The verification report contains only a code review, not actual test execution.
- **Where to look**: Check `verification/{Name}.md` — does it show actual test execution or just a code review? Cross-reference with config.yaml promptwares section.
- **Root cause**: When the tendril CLI was broken, the agent wrote the verification report manually and bypassed the CLI to update plan.yaml directly. The fallback tool (`Update-PlanYaml.ps1`) has since been removed — the CLI is now the only path.
- **Detection**: If a verification name matches a directory under `Promptwares/`, it's delegated and should NOT be self-certified.
- **Fix area**: `Promptwares/ExecutePlan/Program.md` (instruction-level). The CLI is the only supported mechanism for setting verification status.

### Commit Hash Display Mismatch (Fixed 2026-04-26)
- **Symptom**: All commits show "Potentially corrupted commits" in the UI with no messages or file counts, even though the commits exist.
- **Where to look**: `plan.yaml` stores abbreviated 9-char hashes, but `GetCommitSummaries` returns full 40-char hashes. The dictionary lookup in `PlanContentHelpers.BuildCommitRows` fails silently.
- **Root cause**: `GitService.GetCommitSummaries` keyed results by full hash only. The abbreviated hash from plan.yaml never matched.
- **Fix area**: `Services/GitService.cs` — `StoreCommitResult` now stores under both full and abbreviated hashes.

## Log / Output Failures

### Log File Race Condition (Fixed 2026-04-26)
- **Symptom**: A plan's execution log in `Promptwares/{Type}/Logs/` is overwritten by another concurrent execution. The original session's log is permanently lost.
- **Where to look**: Compare the firmware header's log file path (visible in the session JSONL) against what actually exists at that path. If the content is for a different plan, it was overwritten.
- **Root cause**: `FirmwareCompiler.GetNextLogFile` assigned the next number but didn't create the file, so concurrent calls got the same number.
- **Fix area**: `Services/Agents/FirmwareCompiler.cs` — now reserves the file on disk immediately.

### Raw JSONL Keyed by Wrong ID (Fixed 2026-04-26)
- **Symptom**: No `{PlanId}.raw.jsonl` exists, but a `job-{N}.raw.jsonl` does contain the session data.
- **Where to look**: `Promptwares/{Type}/Logs/` — check for files with job IDs instead of plan IDs.
- **Root cause**: `job.AllocatedPlanId` was only set for CreatePlan jobs. Other job types (ExecutePlan, etc.) left it null, so `WriteRawOutputLog` used the ephemeral job ID as filename.
- **Fix area**: `Services/JobLauncher.cs` — now sets `AllocatedPlanId` for all jobs that operate on a plan folder.
