function Get-ConfigYaml {
    param(
        [Parameter(Mandatory=$true)]
        [string]$ProjectName
    )

    # Cache to avoid re-reading config files
    if (-not $global:ConfigCache) {
        $global:ConfigCache = @{}
    }

    if ($global:ConfigCache.ContainsKey($ProjectName)) {
        return $global:ConfigCache[$ProjectName]
    }

    # Try to find config.yaml in the repository
    $configPath = "D:\Repos\_Ivy\Ivy-Framework\src\tendril\Ivy.Tendril.TeamIvyConfig\config.yaml"

    if (Test-Path $configPath) {
        $config = Get-Content $configPath -Raw | ConvertFrom-Yaml
        $global:ConfigCache[$ProjectName] = $config
        return $config
    }

    return $null
}

function Get-LatestRevision {
    param(
        [Parameter(Mandatory=$true)]
        [string]$PlanFolder
    )

    $revisionsPath = Join-Path $PlanFolder "revisions"
    $revisionFiles = Get-ChildItem -Path $revisionsPath -Filter "*.md" | Sort-Object Name -Descending

    if ($revisionFiles.Count -eq 0) {
        return $null
    }

    return $revisionFiles[0]
}

function Get-NextRevisionNumber {
    param(
        [Parameter(Mandatory=$true)]
        [string]$PlanFolder
    )

    $revisionsPath = Join-Path $PlanFolder "revisions"
    $revisionFiles = Get-ChildItem -Path $revisionsPath -Filter "*.md" | Sort-Object Name -Descending

    if ($revisionFiles.Count -eq 0) {
        return 1
    }

    $lastNumber = [int]($revisionFiles[0].BaseName)
    return $lastNumber + 1
}
