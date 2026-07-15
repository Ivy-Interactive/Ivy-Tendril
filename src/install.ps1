<#
.SYNOPSIS
Ivy-Tendril Windows Standalone Installer

.DESCRIPTION
This script downloads and runs the standalone installer for Ivy-Tendril on Windows.
#>

$ErrorActionPreference = "Stop"

Write-Host "=== Ivy-Tendril Installer for Windows ===" -ForegroundColor Blue

$isWinOS = ([System.Environment]::OSVersion.Platform -eq "Win32NT")
if (-not $isWinOS) {
    Write-Host "Error: This script is only for Windows." -ForegroundColor Red
    exit 1
}

Write-Host "`nChecking if GitHub CLI (gh) is installed..." -ForegroundColor Yellow
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host "Error: GitHub CLI (gh) is not installed." -ForegroundColor Red
    Write-Host "Please install the latest version of gh from https://cli.github.com/ and try again." -ForegroundColor Red
    exit 1
}

$latestGhTag = ""
try {
    # Resolve redirect to get latest tag
    $req = [System.Net.WebRequest]::Create("https://github.com/cli/cli/releases/latest")
    $req.AllowAutoRedirect = $false
    $resp = $req.GetResponse()
    $loc = $resp.Headers["Location"]
    $latestGhTag = $loc.Substring($loc.LastIndexOf('/') + 1)
} catch {
    # Fallback to GitHub API
    try {
        $apiResult = Invoke-RestMethod -Uri "https://api.github.com/repos/cli/cli/releases/latest"
        $latestGhTag = $apiResult.tag_name
    } catch {
        Write-Host "Error: Failed to fetch the latest GitHub CLI release tag." -ForegroundColor Red
        exit 1
    }
}
$latestGhVersion = $latestGhTag.TrimStart('v')

$ghVersionInfo = gh --version
if ($ghVersionInfo[0] -match "gh version (\d+\.\d+\.\d+)") {
    $ghVersion = $Matches[1]
} else {
    Write-Host "Error: Failed to parse GitHub CLI version." -ForegroundColor Red
    exit 1
}

Write-Host "Installed gh version: $ghVersion" -ForegroundColor Green
Write-Host "Latest gh version:    $latestGhVersion" -ForegroundColor Green

if ($ghVersion -ne $latestGhVersion) {
    Write-Host "Error: You do not have the latest GitHub CLI (gh) version." -ForegroundColor Red
    Write-Host "Please upgrade gh to version $latestGhVersion and try again." -ForegroundColor Red
    exit 1
}
Write-Host "✓ GitHub CLI (gh) is up to date." -ForegroundColor Green

Write-Host "`nStep 1: Fetching latest release info..." -ForegroundColor Blue

$arch = "x64"
if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq "Arm64") {
    $arch = "arm64"
}

$latestTag = ""
try {
    # Resolve redirect to get latest tag
    $req = [System.Net.WebRequest]::Create("https://github.com/Ivy-Interactive/Ivy-Tendril/releases/latest")
    $req.AllowAutoRedirect = $false
    $resp = $req.GetResponse()
    $loc = $resp.Headers["Location"]
    $latestTag = $loc.Substring($loc.LastIndexOf('/') + 1)
} catch {
    # Fallback to GitHub API
    try {
        $apiResult = Invoke-RestMethod -Uri "https://api.github.com/repos/Ivy-Interactive/Ivy-Tendril/releases/latest"
        $latestTag = $apiResult.tag_name
    } catch {
        Write-Host "Error: Failed to fetch the latest release tag." -ForegroundColor Red
        exit 1
    }
}

$version = $latestTag.TrimStart('v')
Write-Host "Latest version found: $version" -ForegroundColor Green

$fileName = "IvyTendril-$version-win-$arch.exe"
$downloadUrl = "https://github.com/Ivy-Interactive/Ivy-Tendril/releases/download/$latestTag/$fileName"
$tempPath = Join-Path $env:TEMP $fileName

Write-Host "`nStep 2: Downloading installer..." -ForegroundColor Blue
Write-Host "Downloading from: $downloadUrl"
Invoke-WebRequest -Uri $downloadUrl -OutFile $tempPath

Write-Host "`nStep 3: Running installer..." -ForegroundColor Blue
Write-Host "Please follow the setup wizard if it appears." -ForegroundColor Yellow
Start-Process -FilePath $tempPath -Wait

Write-Host "`n=== Installation Started/Complete! ===" -ForegroundColor Green
Write-Host "If Tendril does not start automatically, search for it in your Start menu." -ForegroundColor Blue
