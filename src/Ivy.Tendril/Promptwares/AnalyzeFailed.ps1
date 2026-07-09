#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Finds every failed Bash / PowerShell command across all promptware raw.jsonl logs and dumps them.

.DESCRIPTION
    Each promptware run records a raw stream-json transcript at *.raw.jsonl (one JSON object per line).
    A shell invocation is an assistant "tool_use" block (name Bash/PowerShell/Shell) carrying the command;
    its result comes back later as a user "tool_result" block referencing the same tool_use_id.

    A command counts as FAILED when its tool_result is flagged is_error==true, or its output reports a
    non-zero exit code ("Exit code 1", "Exit code: 2", ...). Failures are matched back to the originating
    command via tool_use_id, so non-shell errors (a failed Read, Grep, etc.) are never misreported.

.PARAMETER Path
    Root to search. Defaults to the script's own directory (the Promptwares folder).

.PARAMETER FullOutput
    Print the complete error output for each failure instead of a trimmed preview.

.EXAMPLE
    ./AnalyzeFailed.ps1
    ./AnalyzeFailed.ps1 -Path ./ExecutePlan -FullOutput
#>
[CmdletBinding()]
param(
    [string] $Path = $PSScriptRoot,
    [switch] $FullOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Tool names that represent a shell command invocation.
$shellTools = @('Bash', 'PowerShell', 'Shell')
# Non-zero exit code reported inside the output text (covers "Exit code 1" and "Exit code: 1").
$exitCodeRegex = 'Exit code:?\s+[1-9]'

$logFiles = Get-ChildItem -Path $Path -Recurse -Filter '*.raw.jsonl' -File | Sort-Object FullName
if (-not $logFiles) {
    Write-Host "No raw.jsonl files found under $Path"
    return
}

$allFailures = [System.Collections.Generic.List[object]]::new()

foreach ($file in $logFiles) {
    # Pass 1: map tool_use_id -> shell command info.
    $commands = @{}
    # Pass 2 collects failing tool_results (may appear before or after we saw the tool_use in a stream,
    # so buffer them and resolve against $commands afterwards).
    $results = [System.Collections.Generic.List[object]]::new()

    foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        try { $obj = $line | ConvertFrom-Json } catch { continue }

        # message.content is an array of blocks on both assistant and user turns.
        # Guard every hop: system/result lines carry no message, and content is
        # sometimes a plain string rather than an array of blocks.
        if ($obj.PSObject.Properties.Name -notcontains 'message') { continue }
        $message = $obj.message
        if ($null -eq $message -or $message.PSObject.Properties.Name -notcontains 'content') { continue }
        $content = $message.content
        if ($null -eq $content -or $content -isnot [System.Array]) { continue }

        foreach ($block in $content) {
            switch ($block.type) {
                'tool_use' {
                    if ($shellTools -contains $block.name) {
                        $toolInput = $block.input
                        $cmdText = if ($toolInput.PSObject.Properties.Name -contains 'command') { $toolInput.command } else { '' }
                        $descText = if ($toolInput.PSObject.Properties.Name -contains 'description') { $toolInput.description } else { '' }
                        $commands[$block.id] = [pscustomobject]@{
                            Tool        = $block.name
                            Command     = $cmdText
                            Description = $descText
                        }
                    }
                }
                'tool_result' {
                    $text = if ($block.content -is [string]) { $block.content } else { ($block.content | Out-String) }
                    $isError = ($block.PSObject.Properties.Name -contains 'is_error' -and $block.is_error -eq $true)
                    $nonZeroExit = ($text -match $exitCodeRegex)
                    if ($isError -or $nonZeroExit) {
                        $results.Add([pscustomobject]@{
                            Id      = $block.tool_use_id
                            Output  = $text
                        })
                    }
                }
            }
        }
    }

    # Resolve failing results against shell commands only.
    foreach ($r in $results) {
        if ($commands.ContainsKey($r.Id)) {
            $cmd = $commands[$r.Id]
            $allFailures.Add([pscustomobject]@{
                File        = $file.FullName
                Tool        = $cmd.Tool
                Command     = $cmd.Command
                Description = $cmd.Description
                Output      = ($r.Output).Trim()
            })
        }
    }
}

if (-not $allFailures) {
    Write-Host "Scanned $($logFiles.Count) log file(s). No failed Bash/PowerShell commands found."
    return
}

# Dump grouped by file.
foreach ($group in $allFailures | Group-Object File) {
    $rel = Resolve-Path -Relative -Path $group.Name -ErrorAction SilentlyContinue
    if (-not $rel) { $rel = $group.Name }
    Write-Host ''
    Write-Host ('=' * 100) -ForegroundColor DarkGray
    Write-Host "$rel  ($($group.Count) failed)" -ForegroundColor Cyan
    Write-Host ('=' * 100) -ForegroundColor DarkGray

    foreach ($f in $group.Group) {
        Write-Host ''
        Write-Host "[$($f.Tool)] $($f.Description)" -ForegroundColor Yellow
        Write-Host "  $ $($f.Command)" -ForegroundColor White
        $out = $f.Output
        if (-not $FullOutput -and $out.Length -gt 800) {
            $out = $out.Substring(0, 800) + "`n  ... [truncated, use -FullOutput for full text]"
        }
        foreach ($ln in ($out -split "`r?`n")) {
            Write-Host "  | $ln" -ForegroundColor DarkYellow
        }
    }
}

# Summary.
Write-Host ''
Write-Host ('-' * 100) -ForegroundColor DarkGray
Write-Host "Total: $($allFailures.Count) failed command(s) across $(($allFailures | Group-Object File).Count) file(s) (scanned $($logFiles.Count) log file(s))." -ForegroundColor Green

Write-Host ''
Write-Host "By tool:" -ForegroundColor Green
$allFailures | Group-Object Tool | Sort-Object Count -Descending | ForEach-Object {
    Write-Host ("  {0,-12} {1}" -f $_.Name, $_.Count)
}
