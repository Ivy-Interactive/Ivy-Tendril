# tendril-debug-promptware

Analyze a promptware execution log to identify issues and improvement opportunities in Tendril, the promptware instructions, memory, or tools.

## Invocation

```
/tendril-debug-promptware <path> <comment>
```

* **path** — Full path to the `.md` log file (e.g., `D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Promptwares\CreatePlan\Logs\00001.md`)
* **comment** — Free-text describing what to look for or what went wrong

## What This Skill Does

1. Reads the execution log (`.md`) and its companion raw output (`.raw.jsonl`)
2. Reconstructs the agent's execution timeline: tool calls, decisions, errors, retries
3. Cross-references with the promptware's Program.md, Memory, and Tools
4. Identifies concrete improvements to Tendril code, promptware instructions, or agent behavior
5. Produces actionable recommendations

## Execution Steps

### Phase 1 — Read the Execution Log

The `.md` log file (produced by `PromptwareLogWriter`) has this structure:

```markdown
# Execution Log {number}

- **Status:** {Completed|Failed|Timeout}
- **Exit Code:** {0|1|N/A}
- **Started:** {timestamp}
- **Completed:** {timestamp}
- **Duration:** {seconds}s
- **Provider:** {claude|copilot|codex|...}

## CLI Command
{full command line}

## Compiled Prompt
{full firmware + program.md + references + custom instructions}

## Final Output
{agent's last text response}
```

Read this file. Extract:
- The promptware type (from the path, e.g., `.../Promptwares/CreatePlan/Logs/...` → `CreatePlan`)
- The program folder (parent of `Logs/`)
- Status, exit code, duration
- The compiled prompt (contains the firmware headers with all args)
- The final output

### Phase 2 — Analyze the Raw JSONL

The companion file is at the same path with `.raw.jsonl` extension (e.g., `00001.raw.jsonl`).

This is Claude's `--output-format stream-json` output. Each line is a JSON object with a `type` field:

| Type | Contents |
|------|----------|
| `system` | System prompt setup |
| `assistant` | Agent response with `content[]` array (text blocks and tool_use blocks) and `usage` (token counts) |
| `tool_result` | Result of a tool call |
| `result` | Final result text |

**Analysis approach (use targeted reads, never read the whole file if large):**

1. Count total lines: `wc -l`
2. Extract tool call patterns:
   - Grep for `"tool_use"` to find all tool calls
   - Count each tool type (Read, Write, Edit, Bash, Grep, Glob)
   - Identify repeated reads of the same file (redundant work)
   - Find failed tool calls (look for `"error"` or `"is_error":true` in tool_result lines)
3. Token usage:
   - Sum `input_tokens`, `output_tokens` from assistant messages
   - Check `cache_read_input_tokens` vs `cache_creation_input_tokens` for cache efficiency
4. Error patterns:
   - Grep for `error`, `failed`, `exception` in tool results
   - Count build-fix-build cycles (consecutive Bash calls with compilation errors)
   - Identify thrashing (read-edit-read-edit on same file)
5. Timeline:
   - First and last timestamps for wall-clock duration
   - Long gaps between messages (rate limiting, slow tools)

### Phase 3 — Cross-Reference with Promptware Source

Read the promptware's source files from the program folder:

| File | Purpose |
|------|---------|
| `Program.md` | The agent's instructions — did it follow them? |
| `Memory/*.md` | Accumulated learnings — is anything missing or wrong? |
| `Tools/*` | Custom tools available — were they used appropriately? |

Check:
- Did the agent follow Program.md instructions in order?
- Did it skip steps or go off-script?
- Are there Memory entries that should have prevented a mistake?
- Are there Tools that should have been used but weren't?
- Did the compiled prompt provide sufficient context?

### Phase 4 — Cross-Reference with Tendril Source

Based on findings, check relevant Tendril source files:

| File | What It Controls |
|------|-----------------|
| `Services/Agents/FirmwareCompiler.cs` | Firmware template, log allocation, prompt compilation |
| `Services/Agents/AgentProviderFactory.cs` | Tool permissions, model/effort resolution |
| `Services/JobLauncher.cs` | Job launch, firmware values, environment setup |
| `Services/JobCompletionHandler.cs` | Post-completion processing, state transitions |
| `Services/Agents/PromptwareLogWriter.cs` | Log writing |
| `Models/JobArgs.cs` | Typed POCO args passed to jobs |

### Phase 5 — Produce Recommendations

Output a structured analysis directly in the conversation (do NOT write files):

```markdown
## Execution Summary

- **Promptware:** {type}
- **Status:** {status} (exit code {code})
- **Duration:** {duration}
- **Tokens:** {input + output} (cache hit: {ratio}%)
- **Tool Calls:** {count} ({breakdown by type})
- **Errors:** {count}

## Timeline

Brief narrative of what the agent did step by step.

## Findings

### {Finding Title}

- **Category:** {Instruction Gap | Memory Gap | Tool Gap | Tendril Bug | Token Waste | Error Loop | Permission Issue}
- **Severity:** {Low | Medium | High | Critical}
- **Evidence:** {specific tool calls, line numbers in JSONL, quotes from output}

{Description of the issue.}

**Root Cause:** {why this happened}
**Recommendation:** {concrete fix — which file, what to change}

---

{Repeat for each finding}

## Concrete Fixes (Priority Order)

1. **[Severity] {file path}**: {what to change and why}
2. ...
```

## Rules

* **Read-only**: Do NOT modify source code, promptware instructions, or memory files. Output recommendations only.
* **Always produce findings**, even if the execution was clean — "executed as expected" is a valid finding.
* **Be specific**: Cite JSONL line ranges, tool call sequences, and exact prompt sections.
* **The user's comment is your guide**: Prioritize investigating what they flagged.
* **Use targeted reads**: JSONL files can be huge. Use grep, offset/limit, and line counts rather than reading entire files.
* **Focus on actionable items**: Every finding should have a concrete recommendation pointing to a specific file.
* **Distinguish agent mistakes from system issues**: An agent going off-script is a Program.md problem; a tool failing is a Tendril infrastructure problem.
