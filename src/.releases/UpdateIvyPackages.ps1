# UpdateIvyPackages.ps1
# Script to update all Ivy.* packages to their latest versions on NuGet

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

try {
    # 2. Find all csproj files
    $csprojFiles = Get-ChildItem -Path (Join-Path $RepoRoot "src") -Filter *.csproj -Recurse
    
    foreach ($file in $csprojFiles) {
        $csprojPath = $file.FullName
        
        # Load XML
        [xml]$xml = Get-Content $csprojPath
        $packageRefs = $xml.SelectNodes("//PackageReference[starts-with(@Include, 'Ivy.') or @Include='Ivy']")
        
        if ($packageRefs -and $packageRefs.Count -gt 0) {
            $packagesToUpdate = @()
            foreach ($ref in $packageRefs) {
                $packagesToUpdate += $ref.Include
            }
            
            # Remove duplicates
            $packagesToUpdate = $packagesToUpdate | Select-Object -Unique
            
            foreach ($pkg in $packagesToUpdate) {
                Write-Host "Updating package '$pkg' in '$($file.Name)'..."
                # Run dotnet add
                dotnet add $csprojPath package $pkg
            }
        }
    }
}
finally {
    # 3. Restore original Directory.Build.props
    if ($originalContent -ne $null -and (Get-Content $propsFile -Raw) -ne $originalContent) {
        Write-Host "Restoring Directory.Build.props..."
        Set-Content -Path $propsFile -Value $originalContent -NoNewline
    }
}

Write-Host "Ivy packages updated successfully!"
