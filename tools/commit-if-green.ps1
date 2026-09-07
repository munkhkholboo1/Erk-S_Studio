# Commit and push ONLY if the test suite passes.
#
# WHY THIS EXISTS. Twice on 2026-09-07 a commit was pushed with a red test,
# both times from a chained one-liner of the shape:
#
#     dotnet test | grep -E "Passed|Failed" && git commit && git push
#
# In that chain the gate is `grep`, and grep succeeds when it FINDS TEXT - it
# says nothing about whether the tests passed. The word "Failed!" satisfies it
# just as well as "Passed!". Explaining that once did not stop it happening
# again the same day, so the fix is not another reminder: it is that the commit
# path no longer has a place to put a chain.
#
# The gate here is the TEST RUNNER'S EXIT CODE and nothing else.
#
# Usage:
#     pwsh tools/commit-if-green.ps1 -MessageFile <path> [-Push]
#
# The message travels in a file so that Cyrillic and newlines survive without
# quoting games - the same reason `git commit -F` exists.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$MessageFile,
    [switch]$Push,
    [string]$Solution = "src"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $MessageFile)) {
    Write-Host "REFUSED: commit message file not found: $MessageFile"
    exit 2
}

Write-Host "Running the suite..."
$testOutput = & dotnet test (Join-Path $repoRoot $Solution) -v q --nologo 2>&1
$testExit = $LASTEXITCODE

# Kept whole, and printed whole on failure. The chain this script replaces also
# swallowed a flaky test's measured value - the one number that would have told
# us what to look at - because the output went through a filter instead of to a
# reader.
$logPath = Join-Path ([System.IO.Path]::GetTempPath()) ("erks-gate-" + [Guid]::NewGuid().ToString("N") + ".log")
$testOutput | Out-File -FilePath $logPath -Encoding utf8

$summary = $testOutput | Where-Object { $_ -match "^(Passed|Failed)!" }
$summary | ForEach-Object { Write-Host "  $_" }

if ($testExit -ne 0) {
    Write-Host ""
    Write-Host "RED - nothing was committed. Exit code $testExit."
    $testOutput | Where-Object { $_ -match "\[FAIL\]|Error Message|was .* on|Assert\." } |
        Select-Object -First 20 |
        ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    Write-Host "Full output kept at: $logPath"
    exit 1
}

Write-Host "GREEN - committing."
& git -C $repoRoot add -A
if ($LASTEXITCODE -ne 0) { Write-Host "git add failed"; exit 1 }

& git -C $repoRoot commit -q -F $MessageFile
if ($LASTEXITCODE -ne 0) { Write-Host "git commit failed"; exit 1 }

if ($Push) {
    & git -C $repoRoot push -q origin main
    if ($LASTEXITCODE -ne 0) { Write-Host "git push failed"; exit 1 }
}

& git -C $repoRoot log --oneline -1
Remove-Item $logPath -ErrorAction SilentlyContinue
exit 0
