param(
    [Parameter(Mandatory = $true)]
    [string]$PlanFolder,
    [Parameter(Mandatory = $true)]
    [string]$Title,
    [Parameter(Mandatory = $true)]
    [string]$Project,
    [Parameter(Mandatory = $true)]
    [string]$Level,
    [Parameter(Mandatory = $true)]
    [string]$InitialPrompt,
    [string]$SessionId = "",
    [string]$SourceUrl = "",
    [string]$ExecutionProfile = "",
    [string[]]$Repos = @(),
    [hashtable]$Verifications = @{},
    [int]$Priority = 0
)

$created = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")

$yaml = @"
state: Draft
project: $Project
level: $Level
title: "$Title"
sessionId: "$SessionId"
repos:
"@

foreach ($repo in $Repos) {
    $yaml += "`n- $repo"
}

$yaml += @"

created: $created
updated: $created
initialPrompt: "$InitialPrompt"
"@

if ($SourceUrl) {
    $yaml += "`nsourceUrl: `"$SourceUrl`""
}

$yaml += @"

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
"@

if ($ExecutionProfile) {
    $yaml += "`nexecutionProfile: $ExecutionProfile"
}

$yamlPath = Join-Path $PlanFolder "plan.yaml"
$yaml | Out-File -FilePath $yamlPath -Encoding utf8 -NoNewline

Write-Host "Created plan.yaml at $yamlPath"
