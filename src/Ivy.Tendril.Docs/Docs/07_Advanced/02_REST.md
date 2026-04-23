---
icon: Globe
searchHints:
  - api
  - rest
  - http
  - endpoint
  - plans
  - inbox
  - authentication
  - X-Api-Key
---

# REST API

<Ingress>
Tendril exposes a REST API for programmatic plan management. All endpoints are available at your Tendril server URL (default `https://localhost:5010`).
</Ingress>

## Authentication

When `api.apiKey` is set in `config.yaml`, all API requests require the `X-Api-Key` header:

```yaml
# config.yaml
api:
  apiKey: "your-secret-key"
```

```bash
curl -H "X-Api-Key: your-secret-key" https://localhost:5010/api/plans
```

If no `apiKey` is configured, all routes are open.

## Plans

### Get Plan

```
GET /api/plans/{planId}
GET /api/plans/{planId}?field=state
```

Returns the full plan object, or a single field value when `?field=` is specified.

### List Plans

```
GET /api/plans?state=Draft&project=MyProject&limit=50
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `state` | string | Filter by plan state |
| `project` | string | Filter by project name |
| `limit` | int | Maximum results (default 50) |

### Update Field

```
PUT /api/plans/{planId}
Content-Type: application/json

{ "field": "state", "value": "Executing" }
```

Supported fields: `state`, `project`, `level`, `title`, `executionProfile`, `initialPrompt`, `sourceUrl`, `priority`.

### Add Repository

```
POST /api/plans/{planId}/repos
Content-Type: application/json

{ "repoPath": "D:\\Repos\\MyRepo" }
```

### Remove Repository

```
DELETE /api/plans/{planId}/repos
Content-Type: application/json

{ "repoPath": "D:\\Repos\\MyRepo" }
```

### Add PR

```
POST /api/plans/{planId}/prs
Content-Type: application/json

{ "prUrl": "https://github.com/org/repo/pull/42" }
```

### Add Commit

```
POST /api/plans/{planId}/commits
Content-Type: application/json

{ "sha": "abc1234def5678" }
```

### Set Verification

```
PUT /api/plans/{planId}/verifications
Content-Type: application/json

{ "name": "DotnetBuild", "status": "Pass" }
```

Valid statuses: `Pending`, `Pass`, `Fail`, `Skipped`.

### Add Log

```
POST /api/plans/{planId}/logs
Content-Type: application/json

{ "action": "ExecutePlan", "summary": "Completed successfully" }
```

## Recommendations

### List Recommendations

```
GET /api/plans/{planId}/recommendations
GET /api/plans/{planId}/recommendations?state=Pending
```

### Add Recommendation

```
POST /api/plans/{planId}/recommendations
Content-Type: application/json

{ "title": "Add tests", "description": "Coverage is low", "impact": "Medium", "risk": "Small" }
```

### Accept Recommendation

```
PUT /api/plans/{planId}/recommendations/{title}/accept
Content-Type: application/json

{ "notes": "Only integration tests" }
```

### Decline Recommendation

```
PUT /api/plans/{planId}/recommendations/{title}/decline
Content-Type: application/json

{ "reason": "Not needed for this scope" }
```

### Remove Recommendation

```
DELETE /api/plans/{planId}/recommendations/{title}
```

## Inbox

### Submit Plan

```
POST /api/inbox
Content-Type: application/json

{ "description": "Fix the login bug", "project": "MyProject", "sourcePath": "D:\\Sessions\\Session1" }
```

Starts a `CreatePlan` job and returns the job ID.

## Job Status

Job status is reported via the CLI (not the REST API). Promptware agents use the `tendril job status` command to update progress:

```bash
tendril job status <job-id> --message "Running verifications..."
tendril job status <job-id> --message "Planning..." --plan-id 01234 --plan-title "My Plan"
```

The `<job-id>` is available as `$TENDRIL_JOB_ID` in all promptware processes.
