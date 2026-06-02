# IncrementVersion.ps1
# Script to parse and increment the patch version in Directory.Build.props

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

$propsFile = Join-Path $RepoRoot "src" "Directory.Build.props"
if (-not (Test-Path $propsFile)) {
    Write-Error "Could not find Directory.Build.props at $propsFile"
    exit 1
}

Write-Host "Reading version from: $propsFile"
$content = Get-Content $propsFile -Raw

# Match <Version>X.Y.Z</Version> using regex to preserve the exact format (spaces, indentation, XML casing)
if ($content -match '<Version>(?<version>[0-9\.]+)</Version>') {
    $oldVersionStr = $Matches['version']
    $version = [version]$oldVersionStr
    
    # Increment the build/patch component (the 3rd element in a 3-part version)
    $newVersion = [version]::new($version.Major, $version.Minor, $version.Build + 1)
    $newVersionStr = $newVersion.ToString()
    
    $updatedContent = $content -replace "<Version>$oldVersionStr</Version>", "<Version>$newVersionStr</Version>"
    Set-Content -Path $propsFile -Value $updatedContent -NoNewline
    
    Write-Host "Successfully incremented version from $oldVersionStr to $newVersionStr in Directory.Build.props"
} else {
    Write-Error "Could not find <Version> element in Directory.Build.props"
    exit 1
}
