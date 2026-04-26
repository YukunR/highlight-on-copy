param([string[]]$Files)

$hasError = $false

foreach ($file in $Files) {
    if (-not (Test-Path $file)) { continue }

    $lines = Get-Content $file -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count - 2; $i++) {
        $prev = $lines[$i].TrimEnd()
        $mid = $lines[$i + 1].TrimEnd()
        $next = $lines[$i + 2].TrimEnd()

        # Match 3-line block divider:
        #   line[i]   = pure ASCII separator  // ---...---
        #   line[i+1] = title line            // ---- Title ----
        #   line[i+2] = pure ASCII separator  // ---...---
        $isSeparator = { param($s) $s -match '^\s*// -{3,}\s*$' }
        $isTitle = { param($s) $s -match '^\s*// ---- .+ ----$' }

        if ((& $isSeparator $prev) -and (& $isTitle $mid) -and (& $isSeparator $next)) {
            if ($prev.Length -ne $mid.Length -or $next.Length -ne $mid.Length) {
                Write-Host "ERROR: ${file}:$($i + 1) block divider lines are not equal length"
                Write-Host "  separator: $($prev.Length) chars | title: $($mid.Length) chars | separator: $($next.Length) chars"
                $hasError = $true
            }
        }
    }
}

if ($hasError) { exit 1 } else { exit 0 }
