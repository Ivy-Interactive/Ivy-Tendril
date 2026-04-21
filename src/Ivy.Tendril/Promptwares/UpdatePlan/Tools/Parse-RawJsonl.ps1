# Parse-RawJsonl.ps1
# Extracts Claude text content from raw.jsonl log files

param(
    [string]$Path,
    [int]$MaxBlocks = 50
)

$blockCount = 0
$inTextBlock = $false
$currentText = ""

Get-Content $Path | ForEach-Object {
    if ($blockCount -ge $MaxBlocks) { return }

    # Skip non-JSON lines
    if (-not $_.StartsWith('{')) { return }

    try {
        $obj = $_ | ConvertFrom-Json

        # Look for content blocks with text
        if ($obj.type -eq 'content_block_start' -and $obj.content_block.type -eq 'text') {
            $inTextBlock = $true
            $currentText = ""
        }
        elseif ($obj.type -eq 'content_block_delta' -and $obj.delta.type -eq 'text_delta') {
            if ($inTextBlock) {
                $currentText += $obj.delta.text
            }
        }
        elseif ($obj.type -eq 'content_block_stop') {
            if ($inTextBlock -and $currentText) {
                Write-Output "=== Text Block $($blockCount + 1) ==="
                Write-Output $currentText
                Write-Output ""
                $blockCount++
            }
            $inTextBlock = $false
            $currentText = ""
        }
    }
    catch {
        # Skip malformed JSON
    }
}
