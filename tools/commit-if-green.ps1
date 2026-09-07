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
# NO `2>&1`. Under PowerShell 5.1 that turns a native command's stderr into
# ErrorRecords, which with $ErrorActionPreference = 'Stop' throws before the
# exit code is ever read. The gate would then die on a RED run instead of
# reporting it - the one path that has to work. Found by writing a second
# script with the same line and watching it break.
$testOutput = & dotnet test (Join-Path $repoRoot $Solution) -v q --nologo
$testExit = $LASTEXITCODE

# The runner's STDOUT, kept whole. Its stderr - which is where xUnit writes its
# "[FAIL] <test name>" lines - is deliberately NOT captured: capturing it under
# PowerShell 5.1 requires `2>&1`, which turns each line into an ErrorRecord and,
# with $ErrorActionPreference = "Stop", kills this script on exactly the runs it
# exists to report. So the failure lines go straight to the console, where the
# reader sees them live, and this file holds the rest.
$logPath = Join-Path ([System.IO.Path]::GetTempPath()) ("erks-gate-" + [Guid]::NewGuid().ToString("N") + ".log")
$testOutput | Out-File -FilePath $logPath -Encoding utf8

$summary = $testOutput | Where-Object { $_ -match "^(Passed|Failed)!" }
$summary | ForEach-Object { Write-Host "  $_" }

if ($testExit -ne 0) {
    Write-Host ""
    Write-Host "RED - nothing was committed. Exit code $testExit."
    # No detail block here on purpose. xUnit's "[FAIL] <name>" lines are on
    # stderr and have already been printed above by the runner itself; a filter
    # over the captured stdout would print either nothing or - as the first
    # version of it did - an analyser WARNING under the "RED" heading, a detail
    # block that disagreed with its own headline.
    Write-Host "The failing test names are in the runner output above."
    Write-Host ""
    Write-Host "Runner stdout kept at: $logPath"
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
