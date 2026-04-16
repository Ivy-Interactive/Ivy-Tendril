---
date: 2026-04-16T12:27:24Z
planId: 03397
request: "The codebase now has multiple similar plan folder parsing utilities:
- `WorktreeLifecycleLogger.ExtractPlanId()` — extracts the plan ID (5 digits)
- `PlanReaderService.ExtractSafeTitle()` — extracts the safe title (after ID)
- PowerShell scripts like `CleanupWorktrees.ps1` duplicate this logic

Consider creating a dedicated static class (e.g., `PlanFolderNameParser`) with reusable methods for both extraction tasks. This would improve maintainability and ensure consistent parsing logic across the codebase.

Location: `src/tendril/Ivy.Tendril/Services/`
"
project: "Tendril"
exitCode: PowerShell Exception: Program 'claude.exe' failed to run: An error occurred trying to start process 'C:\Users\niels\.local\bin\claude.exe' with working directory 'D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Promptwares\MakePlan'. The filename or extension is too long.At D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Promptwares\MakePlan\MakePlan.ps1:91 char:5
+     & $agent.Executable @agentArgs 2>&1 |
+     ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~.
---

# MakePlan Failure

**Plan ID:** 03397
**Request:** The codebase now has multiple similar plan folder parsing utilities:
- `WorktreeLifecycleLogger.ExtractPlanId()` — extracts the plan ID (5 digits)
- `PlanReaderService.ExtractSafeTitle()` — extracts the safe title (after ID)
- PowerShell scripts like `CleanupWorktrees.ps1` duplicate this logic

Consider creating a dedicated static class (e.g., `PlanFolderNameParser`) with reusable methods for both extraction tasks. This would improve maintainability and ensure consistent parsing logic across the codebase.

Location: `src/tendril/Ivy.Tendril/Services/`

**Project:** Tendril
**Exit Code:** PowerShell Exception: Program 'claude.exe' failed to run: An error occurred trying to start process 'C:\Users\niels\.local\bin\claude.exe' with working directory 'D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Promptwares\MakePlan'. The filename or extension is too long.At D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Promptwares\MakePlan\MakePlan.ps1:91 char:5
+     & $agent.Executable @agentArgs 2>&1 |
+     ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~.

## Error Output

```
[tendril] PowerShell Exception: Program 'claude.exe' failed to run: An error occurred trying to start process 'C:\Users\niels\.local\bin\claude.exe' with working directory 'D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Promptwares\MakePlan'. The filename or extension is too long.At D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Promptwares\MakePlan\MakePlan.ps1:91 char:5
```

## Investigation Steps

1. Check if this is a duplicate detection issue - search Plans/ for similar titles
2. Check if code assertion validation failed - review Validate-CodeAssertion output
3. Check if config.yaml project/repo configuration is invalid
4. Review full agent output in MakePlan/Logs/ directory
