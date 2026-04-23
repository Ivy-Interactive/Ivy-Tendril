<#
.SYNOPSIS
    Analyzes a Claude session JSONL file for token usage, tool calls, and error patterns.
.PARAMETER Path
    Path to the .jsonl file
.OUTPUTS
    PSCustomObject with TokenUsage, ToolCalls, Errors, Timestamps
#>
param(
    [Parameter(Mandatory)]
    [string]$Path
)

$lines = Get-Content $Path -Encoding UTF8
$totalInput = 0
$totalOutput = 0
$totalCacheRead = 0
$totalCacheWrite = 0
$toolCalls = @{}
$toolErrors = @()
$failedToolCalls = @()
$fileReads = @{}
$timestamps = @()
$messageCount = 0
$assistantMessages = 0

foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try {
        $obj = $line | ConvertFrom-Json -ErrorAction Stop
    } catch { continue }

    # Extract timestamps
    if ($obj.timestamp) {
        $timestamps += $obj.timestamp
    }

    # Count messages
    $messageCount++

    # Token usage from assistant messages
    if ($obj.type -eq "assistant" -and $obj.message -and $obj.message.usage) {
        $assistantMessages++
        $u = $obj.message.usage
        if ($u.input_tokens) { $totalInput += [long]$u.input_tokens }
        if ($u.output_tokens) { $totalOutput += [long]$u.output_tokens }
        if ($u.cache_read_input_tokens) { $totalCacheRead += [long]$u.cache_read_input_tokens }
        if ($u.cache_creation_input_tokens) { $totalCacheWrite += [long]$u.cache_creation_input_tokens }
    }

    # Tool use from assistant messages
    if ($obj.type -eq "assistant" -and $obj.message -and $obj.message.content) {
        foreach ($block in $obj.message.content) {
            if ($block.type -eq "tool_use") {
                $toolName = $block.name
                if (-not $toolCalls.ContainsKey($toolName)) {
                    $toolCalls[$toolName] = 0
                }
                $toolCalls[$toolName]++

                # Track file reads
                if ($toolName -eq "Read" -and $block.input -and $block.input.file_path) {
                    $fp = $block.input.file_path
                    if (-not $fileReads.ContainsKey($fp)) {
                        $fileReads[$fp] = 0
                    }
                    $fileReads[$fp]++
                }
            }
        }
    }

    # Tool results - check for errors
    if ($obj.type -eq "tool_result" -or ($obj.message -and $obj.message.content)) {
        $content = if ($obj.type -eq "tool_result") { $obj.content }
                   elseif ($obj.message) { $obj.message.content }
                   else { $null }
        if ($content) {
            $contentStr = if ($content -is [string]) { $content }
                         elseif ($content -is [array]) { ($content | ForEach-Object { $_.text }) -join " " }
                         else { $content | ConvertTo-Json -Compress }
            if ($contentStr -match '(?i)(error|failed|exception|could not|cannot|timeout)') {
                if ($obj.type -eq "tool_result") {
                    $snippet = if ($contentStr.Length -gt 200) { $contentStr.Substring(0, 200) + "..." } else { $contentStr }
                    $toolErrors += $snippet
                }
            }
        }
    }
}

# Calculate cache hit ratio
$totalCacheTokens = $totalCacheRead + $totalCacheWrite + $totalInput
$cacheHitRatio = if ($totalCacheTokens -gt 0) { [math]::Round(($totalCacheRead / $totalCacheTokens) * 100, 1) } else { 0 }

# Files read more than once
$duplicateReads = $fileReads.GetEnumerator() | Where-Object { $_.Value -gt 1 } | Sort-Object Value -Descending

# Time analysis
$firstTime = $null
$lastTime = $null
if ($timestamps.Count -gt 0) {
    try {
        $firstTime = [DateTimeOffset]::Parse($timestamps[0]).UtcDateTime
        $lastTime = [DateTimeOffset]::Parse($timestamps[-1]).UtcDateTime
    } catch {}
}

$result = [PSCustomObject]@{
    InputTokens = $totalInput
    OutputTokens = $totalOutput
    CacheReadTokens = $totalCacheRead
    CacheWriteTokens = $totalCacheWrite
    TotalTokens = $totalInput + $totalOutput + $totalCacheRead + $totalCacheWrite
    CacheHitRatio = $cacheHitRatio
    AssistantMessages = $assistantMessages
    TotalMessages = $messageCount
    ToolCalls = ($toolCalls.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { "$($_.Key): $($_.Value)" }) -join ", "
    ToolCallsTotal = ($toolCalls.Values | Measure-Object -Sum).Sum
    DuplicateReads = ($duplicateReads | ForEach-Object { "$($_.Key) (x$($_.Value))" }) -join "; "
    ErrorCount = $toolErrors.Count
    Errors = $toolErrors
    FirstTimestamp = $firstTime
    LastTimestamp = $lastTime
    WallClockSeconds = if ($firstTime -and $lastTime) { [math]::Round(($lastTime - $firstTime).TotalSeconds) } else { $null }
}

$result | Format-List
