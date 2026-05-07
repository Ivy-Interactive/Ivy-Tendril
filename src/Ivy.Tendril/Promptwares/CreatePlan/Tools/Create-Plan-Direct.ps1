param(
    [Parameter(Mandatory = $true)]
    [string]$PlansDirectory,
    [Parameter(Mandatory = $true)]
    [string]$Title,
    [Parameter(Mandatory = $true)]
    [string]$Project,
    [Parameter(Mandatory = $true)]
    [string]$Level,
    [Parameter(Mandatory = $true)]
    [string]$InitialPrompt,
    [string]$ExecutionProfile = "balanced",
    [string[]]$Repos = @(),
    [hashtable]$Verifications = @{},
    [int]$Priority = 0
)

# Lock and increment counter
$counterPath = Join-Path $PlansDirectory ".counter"
$lockPath = "$counterPath.lock"

# Simple file-based locking
$retries = 20
$acquired = $false
for ($i = 0; $i -lt $retries; $i++) {
    try {
        $lockFile = [System.IO.File]::OpenWrite($lockPath)
        $acquired = $true
        break
    }
    catch {
        Start-Sleep -Milliseconds 100
    }
}

if (-not $acquired) {
    Write-Error "Failed to acquire lock on counter file"
    exit 1
}

try {
    # Read current counter
    if (Test-Path $counterPath) {
        $currentId = [int](Get-Content $counterPath -Raw).Trim()
    }
    else {
        $currentId = 0
    }

    # Increment
    $newId = $currentId + 1
    $newId | Set-Content -Path $counterPath -NoNewline

    # Create folder
    $safeTitleRaw = $Title -replace '[^a-zA-Z0-9 ]', ''
    $safeTitleParts = $safeTitleRaw -split '\s+' | Where-Object { $_.Length -gt 0 }
    $safeTitle = ($safeTitleParts -join '') -replace '\s+', ''
    if ($safeTitle.Length -gt 60) {
        $safeTitle = $safeTitle.Substring(0, 60)
    }
    $folderName = "{0:D5}-{1}" -f $newId, $safeTitle
    $planFolder = Join-Path $PlansDirectory $folderName

    New-Item -ItemType Directory -Path $planFolder -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $planFolder "revisions") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $planFolder "logs") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $planFolder "artifacts") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $planFolder "verification") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $planFolder "worktrees") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $planFolder "temp") -Force | Out-Null

    # Create plan.yaml
    $created = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

    $yaml = @"
state: Draft
project: $Project
level: $Level
title: "$Title"
sessionId: ""
repos:
"@

    foreach ($repo in $Repos) {
        $yaml += "`n- $repo"
    }

    $yaml += @"

created: $created
updated: $created
initialPrompt: "$InitialPrompt"
prs: []
commits: []
verifications:
"@

    foreach ($ver in $Verifications.GetEnumerator()) {
        $yaml += @"

- name: $($ver.Key)
  status: $($ver.Value)
"@
    }

    $yaml += @"

relatedPlans: []
dependsOn: []
priority: $Priority
executionProfile: $ExecutionProfile
"@

    $yamlPath = Join-Path $planFolder "plan.yaml"
    $yaml | Out-File -FilePath $yamlPath -Encoding utf8 -NoNewline

    # Output results
    Write-Output "PlanId: $("{0:D5}" -f $newId)"
    Write-Output "Directory: $planFolder"
    Write-Output "Plan created: $folderName"
}
finally {
    # Release lock
    $lockFile.Close()
    $lockFile.Dispose()
    Remove-Item -Path $lockPath -Force -ErrorAction SilentlyContinue
}
