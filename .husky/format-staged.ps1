$Files = @(git diff --cached --name-only --diff-filter=ACMR 2>&1 |
    Where-Object { $_ -match '\.cs$' })
if ($Files.Count -eq 0) { exit 0 }

# Record file hashes before formatting so we can detect changes.
$hashBefore = @{}
foreach ($f in $Files) {
    if (Test-Path $f) {
        $hashBefore[$f] = (Get-FileHash $f -Algorithm SHA256).Hash
    }
}

dotnet format highlight-on-copy.sln --no-restore --include $Files
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Detect files that dotnet format modified.
$reformatted = @($Files | Where-Object {
        (Test-Path $_) -and
        $hashBefore.ContainsKey($_) -and
        (Get-FileHash $_ -Algorithm SHA256).Hash -ne $hashBefore[$_]
    })

if ($reformatted.Count -gt 0) {
    Write-Host ""
    Write-Host "dotnet format reformatted the following files:"
    $reformatted | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    Write-Host "Review the changes, stage them with 'git add', and commit again."
    exit 1
}
