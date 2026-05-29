# Direct Revision File Write Workaround

**Date:** 2026-05-29

**Issue:** When Tendril server ports are already in use (common with running instances), the `tendril plan write-revision` command fails with "Port is already in use" error. This blocks the standard revision writing workflow.

**Workaround:** Write the revision file directly to the plan's Revisions folder instead of using the CLI:

```powershell
# 1. Write content to temporary file
$tempPath = "D:\Plans\temp-revision.md"
$revisionContent | Out-File -FilePath $tempPath -Encoding utf8

# 2. Create Revisions folder (if needed)
New-Item -ItemType "Directory" -Path "D:\Plans\000XX-PlanName\Revisions" -Force

# 3. Copy temp file as 001.md (auto-increment manually if needed)
Copy-Item $tempPath "D:\Plans\000XX-PlanName\Revisions\001.md"

# 4. Clean up
Remove-Item $tempPath
```

**Considerations:**
- Does not trigger any server-side validation or database sync that CLI commands normally do
- Plan already created via CLI before this, so metadata is intact
- Revision number must be manually managed (check for existing files in Revisions/)
- Safe to use when CLI write tasks fail due to port conflicts

**Avoid this for:** Any operations that modify plan.yaml itself — always use `tendril plan set` or related metadata commands directly when possible (they may work better), or wait for port to free up.
