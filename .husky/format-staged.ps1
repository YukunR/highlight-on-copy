$Files = @(git diff --cached --name-only --diff-filter=ACMR 2>&1 |
    Where-Object { $_ -match '\.cs$' })
if ($Files.Count -eq 0) { exit 0 }

# Check if any staged .cs files also have unstaged changes in the working tree.
# If so, stash them so dotnet format only sees the staged content — this
# preserves 'git add -p' partial staging.
$unstagedCs = @(git diff --name-only 2>&1 |
    Where-Object { $Files -contains $_ })
$needStash = $unstagedCs.Count -gt 0

if ($needStash) {
    $stashResult = git stash --keep-index 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERROR: git stash --keep-index failed:"
        $stashResult | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
}

try {
    dotnet format highlight-on-copy.sln --no-restore --include $Files
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # Re-stage the formatted content. The working tree now only contains
    # staged content (unstaged hunks are in the stash), so this cannot
    # accidentally include unstaged changes.
    git add $Files
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    if ($needStash) {
        $popResult = git stash pop 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "WARNING: 'git stash pop' encountered conflicts after dotnet format."
            Write-Host "Your unstaged changes are preserved in the stash (stash@{0})."
            Write-Host "To recover:"
            Write-Host "  1. Resolve any conflict markers in the affected files."
            Write-Host "  2. Run: git stash drop"
            Write-Host "  3. Re-stage and commit."
            Write-Host ""
            $popResult | ForEach-Object { Write-Host "  $_" }
            exit 1
        }
    }
}
