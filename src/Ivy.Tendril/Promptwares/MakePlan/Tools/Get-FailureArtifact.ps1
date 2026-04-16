param(
    [Parameter(Mandatory = $true)]
    [string]$PlanId,
    [Parameter(Mandatory = $true)]
    [string]$Description,
    [string]$Project = "Auto",
    [string]$ExitCodeOrException = "1",
    [string[]]$ErrorLines = @()
)

$failedDir = Join-Path $env:TENDRIL_HOME "Failed"
if (-not (Test-Path $failedDir)) {
    New-Item -ItemType Directory -Path $failedDir | Out-Null
}

$safeTitle = $Description -replace '\[FORCE\]', '' -replace '\[YOLO\]', '' -replace '[^a-zA-Z0-9-]', '-'
$safeTitle = $safeTitle.Substring(0, [Math]::Min(60, $safeTitle.Length))

$failureFile = Join-Path $failedDir "$PlanId-$safeTitle.md"

$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$errorOutput = if ($ErrorLines.Count -gt 0) {
    ($ErrorLines | Select-Object -Last 20) -join "`n"
} else {
    "(No error output captured)"
}

$content = @"
---
date: $now
planId: $PlanId
request: "$Description"
project: "$Project"
exitCode: $ExitCodeOrException
---

# MakePlan Failure

**Plan ID:** $PlanId
**Request:** $Description
**Project:** $Project
**Exit Code:** $ExitCodeOrException

## Error Output

``````
$errorOutput
``````

## Investigation Steps

1. Check if this is a duplicate detection issue - search Plans/ for similar titles
2. Check if code assertion validation failed - review Validate-CodeAssertion output
3. Check if config.yaml project/repo configuration is invalid
4. Review full agent output in MakePlan/Logs/ directory
"@

Set-Content -Path $failureFile -Value $content -Force -Encoding UTF8
Write-Host "Failure artifact written: $failureFile" -ForegroundColor Red
