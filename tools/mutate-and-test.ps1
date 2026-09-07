# Apply one sabotage mutation, run a test filter, and always restore.
#
# WHY THIS EXISTS. Sabotage testing is only worth anything if the sabotage
# actually happened. On 2026-09-07 a mutation run reported three tests
# "surviving" a change that had never been applied: the patcher matched on "\n"
# against a CRLF file, found nothing, replaced nothing, and said nothing. The
# tests were then blamed for a weakness they did not have - and worse, the two
# that WERE weak sat in the same batch and were nearly missed among the noise.
#
# That was the third silent-checker of the day across three products. So the
# fix is not "remember to check": it is that this script CANNOT report a
# survivor without first proving the file changed.
#
# Three proofs, all required:
#   1. the anchor text is present before the edit    - else REFUSED
#   2. the file's hash differs after the edit        - else REFUSED
#   3. the file is restored BYTE FOR BYTE, hash-verified, even on failure
#
# The anchor and replacement travel in FILES, not arguments, so Cyrillic,
# newlines and braces survive without quoting games - the same reason
# commit-if-green.ps1 takes its message from a file.
#
# Usage:
#     pwsh tools/mutate-and-test.ps1 -Target <source file> `
#          -AnchorFile <file with the exact text to replace> `
#          -ReplacementFile <file with the new text> `
#          -Filter <dotnet test --filter expression> `
#          -TestProject <path>
#
# Exit codes:
#     0  the mutation applied AND the tests went RED   - the test bites
#     1  the mutation applied and the tests stayed GREEN - SURVIVOR, look at it
#     2  refused: nothing was measured (anchor missing, no change, bad input)

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$AnchorFile,
    [Parameter(Mandatory = $true)][string]$ReplacementFile,
    [Parameter(Mandatory = $true)][string]$TestProject,
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"

function Read-Text([string]$path) {
    if (-not (Test-Path $path)) {
        Write-Host "REFUSED: file not found: $path"
        exit 2
    }
    # Raw, so line endings are preserved exactly as they sit on disk.
    return [System.IO.File]::ReadAllText((Resolve-Path $path))
}

# Bytes, not text. A read-decode-write round trip is NOT identity: .NET strips
# a UTF-8 BOM on read and does not put one back on write, so restoring a
# BOM-carrying source file through text silently dropped its first three bytes.
# The restore check below caught that on its first BOM'd file - which is the
# whole reason it is a check and not a comment.
$originalBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $Target))
$hasBom = $originalBytes.Length -ge 3 -and
          $originalBytes[0] -eq 0xEF -and $originalBytes[1] -eq 0xBB -and $originalBytes[2] -eq 0xBF
$original = Read-Text $Target
$anchor = Read-Text $AnchorFile
$replacement = Read-Text $ReplacementFile

# The anchor and replacement files almost always end with the trailing newline
# an editor added. Trimming exactly one is what makes them usable by hand.
$anchor = $anchor -replace "(\r?\n)$", ""
$replacement = $replacement -replace "(\r?\n)$", ""

# PROOF 1. Anchor present. Both line-ending forms are tried, because a mismatch
# here is precisely the silent failure this script was written for.
$normalizedAnchor = $anchor
if (-not $original.Contains($anchor)) {
    $crlfAnchor = ($anchor -replace "`r`n", "`n") -replace "`n", "`r`n"
    $lfAnchor = $anchor -replace "`r`n", "`n"
    if ($original.Contains($crlfAnchor)) {
        $normalizedAnchor = $crlfAnchor
    }
    elseif ($original.Contains($lfAnchor)) {
        $normalizedAnchor = $lfAnchor
    }
    else {
        Write-Host "REFUSED: the anchor text is not in $Target."
        Write-Host "         Nothing was mutated, so nothing was measured."
        Write-Host "         (Tried the text as given, as CRLF, and as LF.)"
        exit 2
    }
}

# Match the replacement's line endings to the anchor's, so the edit does not
# quietly convert part of the file.
if ($normalizedAnchor.Contains("`r`n")) {
    $replacement = ($replacement -replace "`r`n", "`n") -replace "`n", "`r`n"
}
else {
    $replacement = $replacement -replace "`r`n", "`n"
}

$beforeHash = (Get-FileHash -Path $Target -Algorithm SHA256).Hash
$occurrences = ([regex]::Matches($original, [regex]::Escape($normalizedAnchor))).Count
$mutated = $original.Replace($normalizedAnchor, $replacement)

$mutatedEncoding = New-Object System.Text.UTF8Encoding($hasBom)

try {
    [System.IO.File]::WriteAllText((Resolve-Path $Target), $mutated, $mutatedEncoding)

    # PROOF 2. The file really changed.
    $afterHash = (Get-FileHash -Path $Target -Algorithm SHA256).Hash
    if ($afterHash -eq $beforeHash) {
        Write-Host "REFUSED: $Target is byte-identical after the edit."
        Write-Host "         The replacement must differ from the anchor."
        exit 2
    }

    $beforeLines = ($original -split "`n").Count
    $afterLines = ($mutated -split "`n").Count
    Write-Host "MUTATED  $Target"
    Write-Host "  occurrences replaced : $occurrences"
    Write-Host "  lines                : $beforeLines -> $afterLines"
    Write-Host "  sha256               : $($beforeHash.Substring(0,12)) -> $($afterHash.Substring(0,12))"
    Write-Host ""

    $arguments = @($TestProject, "-v", "q", "--nologo")
    if ($Filter -ne "") { $arguments += @("--filter", $Filter) }
    # NO `2>&1` HERE. In Windows PowerShell 5.1 redirecting a native
    # command's stderr wraps every line in an ErrorRecord, and with
    # $ErrorActionPreference = 'Stop' that THROWS - so the script blew up on
    # exactly the runs it exists to report: the ones where a test fails and
    # writes to stderr. Measured, on this script's own first red run.
    $output = & dotnet test @arguments
    $testExit = $LASTEXITCODE

    $output | Where-Object { $_ -match "^(Passed|Failed)!" } | ForEach-Object { Write-Host "  $_" }
    Write-Host ""

    if ($testExit -ne 0) {
        Write-Host "CAUGHT - the mutation was applied and a test went red."
        $result = 0
    }
    else {
        # Not "the test is bad" and not "the code is fine" - it is a question,
        # and the script says so rather than answering it.
        Write-Host "SURVIVOR - the mutation was applied and every test stayed green."
        Write-Host "           Either the assertion is not reaching this code, or it is"
        Write-Host "           matching something else. Both are worth reading."
        $result = 1
    }
}
finally {
    # PROOF 3. Restored from the ORIGINAL BYTES, and verified - a sabotage run
    # that leaves the mutation behind, or quietly re-encodes the file, is worse
    # than one that never ran.
    [System.IO.File]::WriteAllBytes((Resolve-Path $Target), $originalBytes)
    $restoredHash = (Get-FileHash -Path $Target -Algorithm SHA256).Hash
    if ($restoredHash -ne $beforeHash) {
        Write-Host ""
        Write-Host "!! RESTORE FAILED for $Target - the working tree still holds the mutation."
        Write-Host "!! Expected $beforeHash, got $restoredHash"
        exit 2
    }
    Write-Host "restored $Target"
}

exit $result
