---
name: tendrillable
description: Find "Tendrillable" GitHub issues — open, recent, code-requiring issues that an agent can plan and one-shot WITHOUT asking clarifying questions, with high probability of success. Classifies a repo's open issues against the Tendrillable rubric and prints a ranked list of issue URLs. Use when asked to find tendrillable issues, source candidates for Tendril, or triage a repo's backlog for one-shottable work. Usage: tendrillable <githubUrl> <amount>.
---

# tendrillable

Given a GitHub repository, find issues that are **Tendrillable**: an open, recent issue that an agent can read, plan, and one-shot into a working code change — with no clarifying questions and a high probability of success. Output a ranked list of issue URLs.

## Invocation

```
/tendrillable <githubUrl> <amount>
```

- `<githubUrl>` — repo URL (`https://github.com/owner/repo`) or `owner/repo`.
- `<amount>` — how many Tendrillable issue URLs to return (default: `10`).

This is a **high-precision** classifier. Returning fewer than `<amount>` is correct and expected when the backlog doesn't have enough qualifying issues. **Never pad the list with marginal issues** — a false positive that an agent can't actually one-shot is worse than a short list.

## The Tendrillable definition

> A Tendrillable issue can be **fully resolved from the repo + the issue text alone** — no human in the loop, no missing context, and no judgment call about *what* to build. The agent cannot ask questions, so anything the issue leaves implicit must be inferable from the issue and the codebase.

### Necessary conditions — ALL must hold
1. **Open** — and not already solved-in-flight (not assigned, no linked/"fixes #" PR, no "fixed in <sha>" comment).
2. **Requires code** — the fix means writing/changing source code. Excludes docs-only, questions, support requests, dependency-bump/release chores, CI-config-only asks.
3. **Unambiguous outcome** — there is effectively one correct end-state. A bug with steps-to-reproduce + expected-vs-actual is ideal; a feature qualifies only if the desired behavior is fully specified.
4. **Localized** — small blast radius (ideally one subsystem / a handful of files). Excludes "redesign X", broad refactors, cross-cutting rewrites.
5. **Verifiable** — success is checkable, ideally by a test the agent can write/run or an obvious behavioral check.
6. **Self-contained** — no external blockers: no credentials/API keys/paid services, no upstream wait, no product/design decision, no special hardware to reproduce.

### Disqualifiers — ANY one fails the issue regardless of score
- An active debate thread (multiple proposed approaches, maintainer disagreement) or labels like `needs discussion`, `rfc`, `wontfix`, `question`, `invalid`, `duplicate`.
- Vague symptom with no reproduction ("crashes sometimes", "feels slow").
- Open-ended investigation (flaky test, perf-regression hunt, "why is X happening?").
- Security-sensitive (wants responsible disclosure).
- Requires tribal/roadmap knowledge not present in the repo.

### Positive signals (rank these higher)
- Labels: `good first issue`, `good-first-issue`, `help wanted`, `bug` (with repro).
- **Recency** — recent issues run against a codebase that still matches HEAD, so the agent's plan won't be invalidated by drift. Prefer newest.
- A clear "expected behavior" / failing snippet / stack trace in the body.
- Healthy repo (builds, has tests) so the agent can validate.

## Execution steps

### Phase 0 — Setup
1. Normalize `<githubUrl>` to `OWNER/REPO`.
2. Default `AMOUNT=10` if not provided.
3. Confirm `gh auth status` succeeds. If not, tell the user to run `! gh auth login` and stop.

### Phase 1 — Pull candidates (mechanical, cheap)
Pull more candidates than requested (newest first) so the judgment pass has room to reject:

```bash
REPO="OWNER/REPO"
AMOUNT=10
CANDIDATES=$(( AMOUNT * 5 < 50 ? 50 : AMOUNT * 5 )); [ "$CANDIDATES" -gt 200 ] && CANDIDATES=200

gh issue list -R "$REPO" --state open --limit "$CANDIDATES" \
  --json number,title,body,labels,assignees,createdAt,updatedAt,comments,url \
| jq -c '
    map(. + {lbl: ([.labels[].name] | map(ascii_downcase))})
    # drop assigned (likely in progress)
    | map(select((.assignees|length) == 0))
    # drop obvious non-code / non-actionable by label
    | map(select((.lbl | any(IN(
        "question","discussion","needs discussion","needs-discussion",
        "documentation","docs","wontfix","wont-fix","duplicate","invalid",
        "rfc","needs info","needs-info","needs more information","stale","blocked"
      ))) | not))
    # newest first
    | sort_by(.createdAt) | reverse
  '
```

Notes:
- `gh issue list` returns **issues only** (PRs are excluded), so no extra PR filtering is needed.
- Keep each candidate's `number`, `title`, `body`, `labels`, `createdAt`, `comments`, `url`.

### Phase 2 — Classify (agent judgment)
For each surviving candidate, **read the title + body** and evaluate against the rubric:

1. Check every **necessary condition** (1–6). If any fails → **REJECT**.
2. Check **disqualifiers**. If any fires → **REJECT**. (Skim the comment count: many comments often signals debate — if `comments` is high, read enough to confirm there's no unresolved disagreement.)
3. If it passes, assign a **Tendrillability score 0–100**:
   - Base on how cleanly the conditions hold (clear repro + expected/actual + localized + testable ≈ 80–100).
   - Apply positive signals (good-first-issue label, recency, failing snippet) as boosts.
   - When genuinely uncertain whether the agent could one-shot it → score it lower or reject. **Bias toward precision.**

Keep enough survivors past this pass to backfill (Phase 2.5 will drop some).

### Phase 2.5 — Verify not solved-in-flight (MANDATORY)
The `gh issue list` label/assignee filter does **not** catch issues that already have a linked PR. An open or merged PR cross-referencing an issue means the work is done or in progress — **never return those**. In practice this catches a meaningful fraction of otherwise-perfect candidates, so it is a hard gate, not an optional check.

For every issue that passed Phase 2 (process the top scorers first; you only need `AMOUNT` survivors), run the timeline check below. Drop any issue whose verdict is `DROP` (has an OPEN/MERGED linked PR, or has become assigned). Then backfill from the next-highest Phase-2 survivors and re-check those, until you have `AMOUNT` `KEEP`s or run out.

```bash
check_pr() {  # usage: check_pr OWNER/REPO ISSUE_NUMBER
  local repo="$1" num="$2" owner="${1%%/*}" name="${1##*/}"
  gh api graphql -f query='
    query($owner:String!,$name:String!,$num:Int!){
      repository(owner:$owner,name:$name){
        issue(number:$num){
          assignees(first:5){ totalCount }
          timelineItems(itemTypes:[CROSS_REFERENCED_EVENT,CONNECTED_EVENT], first:50){
            nodes{ __typename
              ... on CrossReferencedEvent{ source{ __typename ... on PullRequest{ number state } } }
              ... on ConnectedEvent{ subject{ __typename ... on PullRequest{ number state } } } } }
        } } }' -f owner="$owner" -f name="$name" -F num="$num" \
  | jq -r --arg num "$num" '
      .data.repository.issue as $i
      | [ $i.timelineItems.nodes[] | (.source // .subject) | select(.__typename=="PullRequest")
          | select(.state=="OPEN" or .state=="MERGED") | "PR#\(.number)(\(.state))" ] as $prs
      | "#\($num)\tassignees:\($i.assignees.totalCount)\tlinkedPRs:\(if ($prs|length)>0 then ($prs|join(",")) else "none" end)\tVERDICT:\(if ($prs|length)>0 or $i.assignees.totalCount>0 then "DROP" else "KEEP" end)"
    '
}
# Example: for n in 5592 5384 5560; do check_pr navidrome/navidrome $n; done
```

Notes:
- Only OPEN/MERGED linked PRs disqualify — a CLOSED (unmerged) PR means a prior attempt was abandoned, which is fine.
- A merged PR usually means the issue is already fixed but not yet closed; an open PR means someone is mid-flight. Both → `DROP`.

### Phase 3 — Output
1. Sort accepted issues by score descending, tie-break by recency (newest first).
2. Take the top `AMOUNT`.
3. Print a short assessment table (for transparency), then the **deliverable: a clean list of URLs, one per line**.

```
## Tendrillable issues for OWNER/REPO  (N of AMOUNT found)

| score | age | labels | title | url |
|------:|-----|--------|-------|-----|
| 92 | 3d | bug | Crash when … | https://github.com/owner/repo/issues/123 |
| ... |

### URLs
https://github.com/owner/repo/issues/123
https://github.com/owner/repo/issues/456
```

If fewer than `AMOUNT` qualify, say so explicitly and return only the qualifying URLs. If **zero** qualify, report that the backlog has no Tendrillable issues right now and (briefly) why the top candidates were rejected.

## Principles
- **Precision over recall** — every URL returned must be one an agent can realistically one-shot. When in doubt, leave it out.
- **No questions** — the whole point is issues solvable without clarification; if *you* would need to ask the reporter something, it's not Tendrillable.
- **Recent wins** — prefer fresh issues that still match HEAD.
- **Explain rejections** — when the list is short, a one-line reason per top rejected candidate helps the user calibrate the rubric.
