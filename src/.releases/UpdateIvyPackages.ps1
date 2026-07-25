# UpdateIvyPackages.ps1
# Script to update all Ivy.* packages to their latest versions on NuGet
#
# NOTE: this script edits the Version attribute on existing <PackageReference> nodes directly
# instead of shelling out to `dotnet add <csproj> package <pkg>`. `dotnet add package` does not
# know how to update a PackageReference that lives inside a conditioned <ItemGroup> (e.g. the
# IvySource-guarded groups in Ivy.Tendril.csproj) - it inserts a brand-new item into an
# unconditioned group instead, producing a duplicate PackageReference (NU1504 warning) rather
# than bumping the version.

# Find repo root by looking for Directory.Build.props using .NET path APIs
$current = $PSScriptRoot
while ($current -and -not (Test-Path (Join-Path $current "src" "Directory.Build.props"))) {
    $parent = [System.IO.Directory]::GetParent($current)
    if (-not $parent) { break }
    $current = $parent.FullName
}

$RepoRoot = $current
if (-not $RepoRoot) {
    # Fallback to parent of parent of PSScriptRoot
    $RepoRoot = [System.IO.Directory]::GetParent([System.IO.Directory]::GetParent($PSScriptRoot).FullName).FullName
}

Write-Host "Updating Ivy packages in repository: $RepoRoot"

$propsFile = Join-Path $RepoRoot "src" "Directory.Build.props"
if (-not (Test-Path $propsFile)) {
    Write-Error "Could not find Directory.Build.props at $propsFile"
    exit 1
}

# 1. Temporarily modify Directory.Build.props to set IvySource to false
Write-Host "Temporarily disabling IvySource to resolve NuGet packages..."
$originalContent = Get-Content $propsFile -Raw

# Replace using string interpolation or simpler replacement
$pattern = '<IvySource Condition="\x27\$\(IvySource\)\x27 == \x27\x27">true</IvySource>'
$replacement = '<IvySource Condition="' + "'" + '$(IvySource)' + "'" + ' == ' + "'" + "'" + '">false</IvySource>'
$modifiedContent = $originalContent -replace $pattern, $replacement

# Verify if replacement succeeded
if ($modifiedContent -eq $originalContent) {
    # Try alternate single quotes in XML
    $modifiedContent = $originalContent -replace "<IvySource Condition='`"'\$\(IvySource\)' == ''`"'>true</IvySource>", "<IvySource Condition='`"'\$\(IvySource\)' == ''`"'>false</IvySource>"
}

if ($modifiedContent -eq $originalContent) {
    Write-Warning "Could not find IvySource property in Directory.Build.props. Nuget resolution might fail if ProjectReferences are evaluated."
} else {
    Set-Content -Path $propsFile -Value $modifiedContent -NoNewline
}

# Cache of resolved latest-stable versions, keyed by package id, so each package is queried once
# even if it appears in multiple csproj files or multiple ItemGroups within one file.
$versionCache = @{}

function Get-LatestStableVersion {
    param([string]$PackageId)

    if ($versionCache.ContainsKey($PackageId)) {
        return $versionCache[$PackageId]
    }

    $url = "https://api.nuget.org/v3-flatcontainer/$($PackageId.ToLowerInvariant())/index.json"
    try {
        $response = Invoke-RestMethod -Uri $url -ErrorAction Stop
        $stableVersions = $response.versions | Where-Object { $_ -notmatch '-' }
        if (-not $stableVersions -or $stableVersions.Count -eq 0) {
            Write-Warning "No stable versions found for package '$PackageId'; leaving its version untouched."
            $versionCache[$PackageId] = $null
            return $null
        }
        $latest = $stableVersions | ForEach-Object { [System.Management.Automation.SemanticVersion]$_ } | Sort-Object | Select-Object -Last 1
        $latestString = $latest.ToString()
        $versionCache[$PackageId] = $latestString
        return $latestString
    } catch {
        Write-Warning "Failed to resolve latest version for package '$PackageId' from NuGet: $_. Leaving its version untouched."
        $versionCache[$PackageId] = $null
        return $null
    }
}

try {
    # 2. Find all csproj files
    $csprojFiles = Get-ChildItem -Path (Join-Path $RepoRoot "src") -Filter *.csproj -Recurse | Where-Object { $_.Name -ne "Ivy.Tendril.Docs.csproj" }

    foreach ($file in $csprojFiles) {
        $csprojPath = $file.FullName

        # Load XML
        [xml]$xml = Get-Content $csprojPath
        $packageRefs = $xml.SelectNodes("//PackageReference[starts-with(@Include, 'Ivy.') or @Include='Ivy']")

        if ($packageRefs -and $packageRefs.Count -gt 0) {
            $changed = $false

            foreach ($ref in $packageRefs) {
                $pkg = $ref.Include

                if ($pkg -eq "Ivy.Docs.Helpers") {
                    Write-Host "Skipping local-only package '$pkg'..."
                    continue
                }

                $latestVersion = Get-LatestStableVersion -PackageId $pkg
                if (-not $latestVersion) {
                    continue
                }

                if ($ref.Version -ne $latestVersion) {
                    Write-Host "Updating package '$pkg' in '$($file.Name)' to version $latestVersion..."
                    $ref.Version = $latestVersion
                    $changed = $true
                }
            }

            if ($changed) {
                $xml.Save($csprojPath)
            }
        }
    }

    # 3. Duplicate guard: catch a package declared twice under conditions that could both be
    # active at once. This is exactly the class of bug this rewrite replaces - `dotnet add
    # package` used to leave a stray duplicate PackageReference (NU1504) instead of bumping the
    # version in its conditioned ItemGroup. Fail loudly here instead of shipping a build warning.
    $duplicatesFound = $false

    foreach ($file in $csprojFiles) {
        [xml]$xml = Get-Content $file.FullName
        $packageRefs = $xml.SelectNodes("//PackageReference[@Version]")

        $groups = @{}
        foreach ($ref in $packageRefs) {
            $include = $ref.Include
            $itemGroup = $ref.ParentNode
            $condition = $itemGroup.GetAttribute("Condition")
            if (-not $groups.ContainsKey($include)) {
                $groups[$include] = @()
            }
            $groups[$include] += $condition
        }

        foreach ($include in $groups.Keys) {
            $conditions = $groups[$include]
            if ($conditions.Count -le 1) { continue }

            # Two or more unconditioned groups, or an unconditioned group plus any conditioned
            # group, can both be active simultaneously -> duplicate. Two groups conditioned on
            # mutually exclusive values (e.g. == 'true' and != 'true') are fine.
            $unconditionedCount = ($conditions | Where-Object { -not $_ }).Count
            $isConflict = $unconditionedCount -ge 2 -or ($unconditionedCount -eq 1 -and $conditions.Count -gt 1)

            if ($isConflict) {
                Write-Error "Duplicate PackageReference '$include' found in '$($file.FullName)' under conditions that can both be active: $($conditions -join ', ')"
                $duplicatesFound = $true
            }
        }
    }

    if ($duplicatesFound) {
        exit 1
    }
}
finally {
    # 4. Restore original Directory.Build.props
    if ($originalContent -ne $null -and (Get-Content $propsFile -Raw) -ne $originalContent) {
        Write-Host "Restoring Directory.Build.props..."
        Set-Content -Path $propsFile -Value $originalContent -NoNewline
    }
}

Write-Host "Ivy packages updated successfully!"
