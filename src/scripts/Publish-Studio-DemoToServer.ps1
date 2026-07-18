[CmdletBinding()]
param(
    [string]$ReleaseVersion = "V0.001.2",

    [Parameter(Mandatory = $true)]
    [string]$ReleaseNotes,

    [string]$LicenseServerRoot = "D:\ErkS-Server\data-root",

    [switch]$Required
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

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

    return @(ConvertFrom-Json -InputObject $raw)
}

$versions = Resolve-ReleaseVersion -Value $ReleaseVersion
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Split-Path -Parent $scriptRoot
$productRoot = Split-Path -Parent $sourceRoot
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
$history = @($historyEntry) + @(
    Read-HistoryFile -Path $historyPath |
        Where-Object { -not [string]::Equals($_.Version, $versions.Metadata, [StringComparison]::OrdinalIgnoreCase) }
)
Write-JsonFile -Path $historyPath -Value $history

Write-Host "Published Erk-S Studio Demo $($versions.Artifact) to $serverRoot"
Write-Host "  Setup: $downloadPath"
Write-Host "  Update: $updatePath"
Write-Host "  SHA256: $downloadHash"
