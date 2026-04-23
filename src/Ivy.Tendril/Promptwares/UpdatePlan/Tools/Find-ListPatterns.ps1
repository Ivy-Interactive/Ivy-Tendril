# Find-ListPatterns.ps1
# Searches for list-like patterns in Claude raw.jsonl output

param(
    [string]$Path
)

$patterns = @()

Get-Content $Path | ForEach-Object {
    if (-not $_.StartsWith('{')) { return }

    try {
        $obj = $_ | ConvertFrom-Json

        # Extract text from various message structures
        $text = $null

        if ($obj.type -eq 'assistant' -and $obj.message.content) {
            foreach ($block in $obj.message.content) {
                if ($block.type -eq 'text' -and $block.text) {
                    $text = $block.text
                }
            }
        }

        if ($text) {
            # Look for patterns: "text:\nItem\nItem" (colon followed by newlines and lines without bullets)
            if ($text -match ':\s*\n([A-Z][^\n]+\n)+') {
                $match = $text -match '(.{0,50}:\s*\n(?:[A-Z][^\n]+\n){2,})'
                if ($match) {
                    $patterns += [PSCustomObject]@{
                        Pattern = $Matches[0]
                        HasBullets = ($text -match ':\s*\n\s*[-*+]\s')
                    }
                }
            }
        }
    }
    catch {
        # Skip malformed JSON
    }
}

if ($patterns.Count -gt 0) {
    Write-Output "Found $($patterns.Count) list-like patterns:"
    $patterns | ForEach-Object {
        Write-Output "---"
        Write-Output "Has bullets: $($_.HasBullets)"
        Write-Output $_.Pattern
        Write-Output ""
    }
} else {
    Write-Output "No list-like patterns found"
}
