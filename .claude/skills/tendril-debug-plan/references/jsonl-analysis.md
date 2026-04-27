# JSONL Session Analysis Reference

How to efficiently analyze Claude session JSONL files from Tendril plan executions.

## File Location

Session JSONL files are stored at:
```
~/.claude/projects/{project-hash}/{SessionId}.jsonl
```

Find by session ID:
```bash
find ~/.claude/projects -name "{SessionId}.jsonl"
```

The SessionId is found in plan log entries at `{PlanFolder}/logs/*.md`.

## JSONL Structure

Each line is a JSON object. Key message types:

### Assistant Messages (token usage + tool calls)
```json
{
  "type": "assistant",
  "message": {
    "usage": {
      "input_tokens": 12345,
      "output_tokens": 678,
      "cache_read_input_tokens": 9000,
      "cache_creation_input_tokens": 3000
    },
    "content": [
      {"type": "text", "text": "..."},
      {"type": "tool_use", "id": "toolu_xxx", "name": "Read", "input": {"file_path": "..."}}
    ]
  }
}
```

### Tool Results
```json
{
  "type": "tool_result",
  "tool_use_id": "toolu_xxx",
  "content": "file contents or error..."
}
```

## Analysis Techniques

### Quick Token Summary (PowerShell)
Use the existing tool:
```powershell
& "$REPOS_HOME/Ivy-Tendril/src/Ivy.Tendril.TeamIvyConfig/Promptwares/PlanEvaluator/Tools/Analyze-SessionJsonl.ps1" -Path "path/to/session.jsonl"
```

### Manual JSONL Scanning
For large files, don't read the whole thing. Instead:

```bash
# Count total lines (messages)
wc -l session.jsonl

# Find error patterns
grep -i '"error\|"failed\|"exception' session.jsonl | head -20

# Count tool types
grep '"tool_use"' session.jsonl | grep -oP '"name":\s*"\K[^"]+' | sort | uniq -c | sort -rn

# Find duplicate file reads
grep '"Read"' session.jsonl | grep -oP '"file_path":\s*"\K[^"]+' | sort | uniq -c | sort -rn | head -10

# Token usage per assistant message
grep '"assistant"' session.jsonl | grep -oP '"input_tokens":\s*\K\d+' | awk '{s+=$1} END {print "Total input:", s}'
```

### Reading Large JSONL
Use offset/limit on the Read tool rather than reading entire files:
- Read first 50 lines for session start context
- Read last 50 lines for final outcome
- Grep for specific patterns in between

## Key Metrics

| Metric | Healthy | Warning | Critical |
|--------|---------|---------|----------|
| Cache hit ratio | >60% | 30-60% | <30% |
| Duplicate file reads | 0-2 | 3-5 | >5 |
| Build-fix cycles | 0-1 | 2-3 | >3 |
| Total tokens (ExecutePlan) | <200k | 200-400k | >400k |
| Wall-clock time | <10min | 10-20min | >20min |
| Failed tool calls | 0-3 | 4-8 | >8 |

## Cascading Failure Pattern

A single infrastructure failure (e.g., broken CLI shim) can cascade through the entire execution:

1. **CLI broken** → agent falls back to PowerShell tools
2. **Fallback tools lack guardrails** → agent self-certifies delegated verifications
3. **Self-certification** → verification marked Pass without actual testing
4. **Plan appears complete** → moves to PR creation with unverified code

When analyzing a plan with unexpected results, trace backwards from the symptom:
- First check if the tendril CLI was available (search JSONL for CLI errors)
- If CLI failed, check which fallback paths the agent took
- Check whether those fallback paths have the same enforcement as the CLI
