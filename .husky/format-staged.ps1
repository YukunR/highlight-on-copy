$Files = @(git diff --cached --name-only --diff-filter=ACMR 2>&1 |
           Where-Object { $_ -match '\.cs$' })
if ($Files.Count -eq 0) { exit 0 }

# Stash unstaged changes while keeping the index intact.
# This ensures dotnet format only sees the staged content, so partial
# staging via 'git add -p' is respected after the hook runs.
$stashOutput = git stash --keep-index 2>&1
$didStash = $stashOutput -notmatch "No local changes to save"

try {
    dotnet format highlight-on-copy.sln --no-restore --include $Files
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # Working tree now contains only the originally staged content (formatted),
    # so git add here cannot accidentally include unstaged hunks.
    git add $Files
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    if ($didStash) {
        git stash pop
    }
}
