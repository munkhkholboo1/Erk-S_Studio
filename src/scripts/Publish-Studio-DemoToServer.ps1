[CmdletBinding()]
param(
    [string]$ReleaseVersion = "",

    # Not Mandatory on purpose. A mandatory parameter makes PowerShell stop and
    # prompt "ReleaseNotes:", and whatever is typed next becomes the value -
    # including a command meant for the shell, typed by someone who thought the
    # script had already finished. This text is published on the public release
    # page, so that prompt is a way for a stray line to end up on the website,
    # and answering it also lets the upload continue when the intent was to
    # abort. Both happened on 2026-08-25. Missing notes now stop the run with an
    # explanation instead of asking a question.
    [string]$ReleaseNotes = "",

    [string]$LicenseServerRoot = "D:\ErkS-Server\data-root",

    [switch]$Required
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    throw @'
Release notes are required and this script will not ask for them.

Pass them on the command line, in quotes:
  .\src\scripts\Publish-Studio-DemoToServer.ps1 -ReleaseNotes "<юу өөрчлөгдсөн>"

They are shown to every user on the public release page, so they have to be
written deliberately rather than typed into a prompt.
'@
}


function Get-RequiredStudioVersionProperty {
    param(
        [Parameter(Mandatory = $true)][xml]$Document,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $Node = $Document.SelectSingleNode("/Project/PropertyGroup/$PropertyName")
    $Value = if ($null -eq $Node) { "" } else { $Node.InnerText.Trim() }
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Studio version property '$PropertyName' is missing from '$Path'."
    }

    return $Value
}

function Resolve-ReleaseVersion {
    param([Parameter(Mandatory = $true)][string]$Value)

    $text = $Value.Trim()
    if ($text.StartsWith("Demo ", [StringComparison]::OrdinalIgnoreCase)) {
        $text = $text.Substring(5).Trim()
    }
    if ($text -notmatch '^[vV]?(\d+\.\d{3}(?:\.\d+)?)$') {
        throw "ReleaseVersion must use V0.001 or V0.001.1 format."
    }

    return [pscustomobject]@{
        Artifact = "V$($Matches[1])"
        Metadata = "v$($Matches[1])"
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Value
    )

    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    $temporaryPath = "$Path.tmp"
    ConvertTo-Json -InputObject $Value -Depth 16 |
        Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Read-HistoryFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return @()
    }

    $raw = Get-Content -Raw -LiteralPath $Path
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return @()
    }

    return @(Expand-HistoryEntry -Value (ConvertFrom-Json -InputObject $raw))
}

function Expand-HistoryEntry {
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) {
        return
    }
    if ($Value -is [System.Array]) {
        foreach ($item in $Value) {
            Expand-HistoryEntry -Value $item
        }
        return
    }

    $versionProperty = $Value.PSObject.Properties["Version"]
    if ($null -ne $versionProperty -and -not [string]::IsNullOrWhiteSpace([string]$versionProperty.Value)) {
        Write-Output $Value
        return
    }

    # PowerShell 5 can serialize a returned array as { value, Count }.
    $wrappedValueProperty = $Value.PSObject.Properties["value"]
    if ($null -ne $wrappedValueProperty) {
        Expand-HistoryEntry -Value $wrappedValueProperty.Value
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Split-Path -Parent $scriptRoot
$productRoot = Split-Path -Parent $sourceRoot
$versionPropsPath = Join-Path $sourceRoot "Studio.Version.props"
if (-not (Test-Path -LiteralPath $versionPropsPath -PathType Leaf)) {
    throw "Authoritative Studio version file was not found: $versionPropsPath"
}
[xml]$versionProps = Get-Content -LiteralPath $versionPropsPath -Raw
$publishedVersion = Get-RequiredStudioVersionProperty `
    -Document $versionProps `
    -PropertyName "StudioPublishedVersion" `
    -Path $versionPropsPath
if ($publishedVersion -notmatch '^\d+\.\d{3}(?:\.\d+)?$') {
    throw "StudioPublishedVersion '$publishedVersion' has an invalid release format."
}
$AuthoritativeReleaseVersion = "V$publishedVersion"

if ([string]::IsNullOrWhiteSpace($ReleaseVersion)) {
    $ReleaseVersion = $AuthoritativeReleaseVersion
}
else {
    $requestedVersions = Resolve-ReleaseVersion -Value $ReleaseVersion
    if (-not $requestedVersions.Artifact.Equals(
            $AuthoritativeReleaseVersion,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "ReleaseVersion '$ReleaseVersion' does not match authoritative Studio.Version.props value '$AuthoritativeReleaseVersion'."
    }
    $ReleaseVersion = $AuthoritativeReleaseVersion
}

$versions = Resolve-ReleaseVersion -Value $ReleaseVersion
$serverRoot = [IO.Path]::GetFullPath($LicenseServerRoot)
$driveRoot = [IO.Path]::GetPathRoot($serverRoot).TrimEnd('\', '/')
if ($serverRoot.TrimEnd('\', '/').Equals($driveRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to publish to a drive root: $serverRoot"
}

$buildRoot = Join-Path $productRoot "builds\product\Demo-$($versions.Artifact)"
$setupSource = Join-Path $buildRoot "ErkS_Studio_Demo_$($versions.Artifact)_Setup.exe"
$releaseManifestPath = Join-Path $buildRoot "release.json"
foreach ($requiredPath in @($setupSource, $releaseManifestPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Studio release artifact was not found: $requiredPath"
    }
}

$releaseManifest = Get-Content -Raw -LiteralPath $releaseManifestPath | ConvertFrom-Json
if (-not [string]::Equals(
        [string]$releaseManifest.displayVersion,
        "Demo $($versions.Artifact)",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release manifest version does not match $($versions.Artifact)."
}
if ($releaseManifest.productDataIncluded -ne $false -or $releaseManifest.devUpdateIncluded -ne $false) {
    throw "Release manifest failed the product-data or DevUpdate safety gate."
}

$productCode = "ErkS.Studio"
$productDataRoot = Join-Path $serverRoot "data\products\$productCode"
$downloadsRoot = Join-Path $serverRoot "downloads\$productCode"
$updatesRoot = Join-Path $serverRoot "updates\$productCode"
$downloadName = "ErkS_Studio_Demo_Setup_$($versions.Metadata).exe"
$updateName = "ErkS_Studio_Demo_Update_$($versions.Metadata).exe"
$downloadPath = Join-Path $downloadsRoot $downloadName
$updatePath = Join-Path $updatesRoot $updateName

foreach ($directory in @($productDataRoot, $downloadsRoot, $updatesRoot)) {
    [IO.Directory]::CreateDirectory($directory) | Out-Null
}

# One version number must never name two different builds.
#
# On 2026-08-25 a build of V0.001.51 was produced under the name V0.001.50 and
# published over the real V0.001.50, which overwrote it on the server and left
# nothing to roll back to. Nothing checked, because the file name matched.
# A name collision with different bytes is always a mistake, so it stops here
# rather than being resolved by whoever copied last.
$incomingHash = (Get-FileHash -LiteralPath $setupSource -Algorithm SHA256).Hash
foreach ($existing in @($downloadPath, $updatePath)) {
    if (-not (Test-Path -LiteralPath $existing -PathType Leaf)) {
        continue
    }

    $existingHash = (Get-FileHash -LiteralPath $existing -Algorithm SHA256).Hash
    if ([string]::Equals($existingHash, $incomingHash, [StringComparison]::OrdinalIgnoreCase)) {
        continue
    }

    throw @"
$($versions.Metadata) is already published with different content.

  on the server : $existingHash
  about to copy : $incomingHash
  path          : $existing

Publishing this would replace a release that users may already be running, and
the version number would no longer identify what they have. Check whether the
build was made under the wrong version number - Studio.Version.props names the
build folder - and republish under its own number.
"@
}

Write-Host "Release notes to publish:" -ForegroundColor Cyan
Write-Host $ReleaseNotes.Trim()

Copy-Item -LiteralPath $setupSource -Destination $downloadPath -Force
Copy-Item -LiteralPath $setupSource -Destination $updatePath -Force

$downloadHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
$updateHash = (Get-FileHash -LiteralPath $updatePath -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not $downloadHash.Equals($updateHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Studio setup and update payload hashes do not match."
}
if (-not $downloadHash.Equals(([string]$releaseManifest.sha256).ToLowerInvariant(), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Published Studio payload hash does not match release.json."
}

$installerMetadata = [ordered]@{
    ProductCode = $productCode
    IsUpdateAvailable = $false
    Version = $versions.Metadata
    DownloadUrl = "/downloads/$productCode/$downloadName"
    Sha256 = $downloadHash
    ReleaseNotes = $ReleaseNotes.Trim()
    IsRequired = [bool]$Required
    RevitVersion = ""
    AutoCADVersion = ""
}
$updateMetadata = [ordered]@{
    ProductCode = $productCode
    IsUpdateAvailable = $false
    Version = $versions.Metadata
    DownloadUrl = "/updates/$productCode/$updateName"
    Sha256 = $updateHash
    ReleaseNotes = $ReleaseNotes.Trim()
    IsRequired = [bool]$Required
    RevitVersion = ""
    AutoCADVersion = ""
}

Write-JsonFile -Path (Join-Path $productDataRoot "latest-installer.json") -Value $installerMetadata
Write-JsonFile -Path (Join-Path $productDataRoot "latest-update.json") -Value $updateMetadata

$historyPath = Join-Path $productDataRoot "update-history.json"
$historyEntry = [ordered]@{
    Version = $versions.Metadata
    DownloadUrl = $updateMetadata.DownloadUrl
    Sha256 = $updateHash
    ReleaseNotes = $updateMetadata.ReleaseNotes
    IsRequired = $updateMetadata.IsRequired
    RevitVersion = ""
    PublishedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
$previousHistory = @(Read-HistoryFile -Path $historyPath)
$history = @($historyEntry) + @(
    $previousHistory |
        Where-Object { -not [string]::Equals($_.Version, $versions.Metadata, [StringComparison]::OrdinalIgnoreCase) }
)

# Keep the record this is about to overwrite.
#
# update-history.json holds the authoritative hash of every release, and this
# line replaces it. When V0.001.51's build was published under V0.001.50's name
# on 2026-08-25, the real V0.001.50 hash was overwritten here and the only way
# anyone could tell was that it had been read minutes earlier - the file kept no
# trace of what it used to say.
#
# The server has the same guard on its own admin path, but that path is not the
# one releases actually travel; this script is. A snapshot here is what makes
# the guard cover the real route.
#
# Failing to take the copy must not stop a release: losing the backup is worse
# than not having taken it, but neither is worse than a release that cannot go
# out.
try {
    if (Test-Path -LiteralPath $historyPath -PathType Leaf) {
        $snapshotRoot = Join-Path $productDataRoot "history-snapshots"
        [IO.Directory]::CreateDirectory($snapshotRoot) | Out-Null
        $stamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss")
        Copy-Item -LiteralPath $historyPath `
            -Destination (Join-Path $snapshotRoot "update-history-$stamp.json") -Force

        # A version whose hash changes was already published with different
        # content. That is always a mistake, and it is the one thing a reader of
        # these snapshots would most want pointed out.
        $previousEntry = $previousHistory |
            Where-Object { [string]::Equals($_.Version, $versions.Metadata, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        if ($null -ne $previousEntry -and
            -not [string]::Equals([string]$previousEntry.Sha256, $updateHash, [StringComparison]::OrdinalIgnoreCase)) {
            @(
                "version    : $($versions.Metadata)"
                "was        : $($previousEntry.Sha256)"
                "now        : $updateHash"
                "writtenAt  : $([DateTimeOffset]::UtcNow.ToString('o'))"
            ) | Out-File -FilePath (Join-Path $snapshotRoot "update-history-$stamp-hash-changed.txt") -Encoding utf8
            Write-Warning "$($versions.Metadata) was already published with a different hash - see history-snapshots."
        }
    }
} catch {
    Write-Warning "Could not snapshot update-history.json: $($_.Exception.Message)"
}

Write-JsonFile -Path $historyPath -Value $history

# Record the release in the version ledger.
#
# STUDIO-RELEASE-MANIFEST-INDEX.json is the answer to "which bytes were ever
# called V0.001.n". It was maintained by hand, and by 2026-08-25 it had stopped
# at V0.001.52 while 53, 54 and 55 were live - a gap nothing reported, because
# forgetting to add a row looks exactly like a row that was never due.
#
# The hash written here is measured from the file that was actually published a
# few lines above, not copied out of release.json. A ledger that repeats a claim
# can only confirm the claim; one that measures can contradict it.
#
# Failing to record must not fail a release that has already gone out - the
# upload is done by this point and reversing it would be worse. The warning is
# the loud part.
try {
    $ledgerPath = Join-Path $productRoot "docs\STUDIO-RELEASE-MANIFEST-INDEX.json"
    if (-not (Test-Path -LiteralPath $ledgerPath -PathType Leaf)) {
        throw "The release ledger is missing: $ledgerPath"
    }

    # ConvertFrom-Json hands a JSON array to the pipeline as ONE object rather
    # than as its elements, so @(...) around it wraps instead of unrolling and
    # every existing row collapses into a single entry. Writing that back turns
    # a ledger of fifty-six releases into a file with one. Assign first, then
    # enumerate.
    $ledgerJson = Get-Content -Raw -LiteralPath $ledgerPath | ConvertFrom-Json
    $ledger = New-Object System.Collections.Generic.List[object]
    foreach ($row in $ledgerJson) { $ledger.Add($row) }

    $ledgerCountBefore = $ledger.Count
    if ($ledgerCountBefore -lt 1) {
        throw "The release ledger read back empty; refusing to overwrite it."
    }

    $alreadyRecorded = $false
    foreach ($row in $ledger) {
        if ([string]::Equals([string]$row.version, $versions.Artifact, [StringComparison]::OrdinalIgnoreCase)) {
            $alreadyRecorded = $true
            break
        }
    }

    if ($alreadyRecorded) {
        Write-Host "  Ledger: $($versions.Artifact) already recorded"
    }
    else {
        $ledger.Add([pscustomobject][ordered]@{
            version = $versions.Artifact
            setupFile = Split-Path -Leaf $setupSource
            sha256 = $downloadHash.ToUpperInvariant()
            sizeBytes = (Get-Item -LiteralPath $downloadPath).Length
            generatedAtUtc = [string]$releaseManifest.generatedAtUtc
            publisher = [string]$releaseManifest.publisher
            signingCertificateThumbprint = [string]$releaseManifest.signingCertificateThumbprint
            signingCertificateSha256 = [string]$releaseManifest.signingCertificateSha256
            signingTrust = [string]$releaseManifest.signingTrust
            recovered = $false
        })

        if ($ledger.Count -ne ($ledgerCountBefore + 1)) {
            throw "The ledger would have gone from $ledgerCountBefore rows to $($ledger.Count); refusing to write."
        }

        Write-JsonFile -Path $ledgerPath -Value $ledger.ToArray()
        Write-Host "  Ledger: recorded $($versions.Artifact), $($ledger.Count) releases"
    }
} catch {
    Write-Warning "Could not record the release in the version ledger: $($_.Exception.Message)"
}

Write-Host "Published Erk-S Studio Demo $($versions.Artifact) to $serverRoot"
Write-Host "  Setup: $downloadPath"
Write-Host "  Update: $updatePath"
Write-Host "  SHA256: $downloadHash"
