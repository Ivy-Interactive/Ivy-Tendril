# Utils.ps1 - Shared utility functions for MakePr promptware

$script:configCache = $null
$script:configPath = "D:\Repos\_Ivy\Ivy-Framework\src\tendril\Ivy.Tendril.TeamIvyConfig\config.yaml"

function Get-ConfigYaml {
    <#
    .SYNOPSIS
    Reads and caches the project configuration from config.yaml

    .DESCRIPTION
    Returns the parsed config.yaml with caching to avoid repeated file reads.
    The config includes project definitions with repos and their prRule settings.

    .EXAMPLE
    $config = Get-ConfigYaml
    $agentProject = $config.projects | Where-Object { $_.name -eq 'Agent' }
    $ivyAgentRepo = $agentProject.repos | Where-Object { $_.path -eq 'D:\Repos\_Ivy\Ivy-Agent' }
    $prRule = $ivyAgentRepo.prRule  # Returns 'yolo' or 'default'
    #>

    if ($null -eq $script:configCache) {
        if (-not (Test-Path $script:configPath)) {
            throw "Config file not found at $script:configPath"
        }

        # Read and parse YAML using PowerShell-Yaml module or simple parsing
        # For now, use ConvertFrom-Yaml if available, otherwise use a basic approach
        $yamlContent = Get-Content $script:configPath -Raw

        if (Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue) {
            $script:configCache = ConvertFrom-Yaml $yamlContent
        } else {
            # Fallback: use powershell-yaml or yq if available
            # If neither is available, throw an error
            throw "PowerShell-Yaml module not found. Install with: Install-Module -Name powershell-yaml"
        }
    }

    return $script:configCache
}

function Get-RepoConfig {
    <#
    .SYNOPSIS
    Gets the repository configuration for a specific repo path

    .PARAMETER ProjectName
    The name of the project (e.g., 'Agent', 'Framework')

    .PARAMETER RepoPath
    The full path to the repository

    .EXAMPLE
    $repoConfig = Get-RepoConfig -ProjectName 'Agent' -RepoPath 'D:\Repos\_Ivy\Ivy-Agent'
    $prRule = $repoConfig.prRule
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ProjectName,

        [Parameter(Mandatory=$true)]
        [string]$RepoPath
    )

    $config = Get-ConfigYaml
    $project = $config.projects | Where-Object { $_.name -eq $ProjectName }

    if (-not $project) {
        throw "Project '$ProjectName' not found in config"
    }

    $repo = $project.repos | Where-Object { $_.path -eq $RepoPath }

    if (-not $repo) {
        throw "Repo '$RepoPath' not found in project '$ProjectName'"
    }

    return $repo
}

# Export-ModuleMember is only valid when Utils.ps1 is imported as a module
# When dot-sourcing (. ./Utils.ps1), functions are automatically available
# Keeping this commented out to avoid errors during dot-sourcing
# Export-ModuleMember -Function Get-ConfigYaml, Get-RepoConfig
