param(
    # Where the signed package of each library version lives - every product copies these bytes.
    [string]$OutputRoot = "E:\Erk-S Platform\_shared\erks-updates",
    [string]$CodeSigningThumbprint = $env:ERKS_CODE_SIGN_CERT_THUMBPRINT
)

# ErkS.Updates, signed ONCE per library version (Master/PFA, 2026-10-26: an Authenticode timestamp and
# a commit stamp made ErkS.Updates.dll and the applier «changed» in every product's every update).
# Builds the library (net48, net8.0, net10.0) and the applier deterministically, signs the ErkS.*
# binaries, and writes <OutputRoot>\<version>\ with hashes.json. A version already there with the same
# unsigned bytes is left as it is; with other bytes the run stops - bump <Version> in the csproj.
# Products (Studio, CGA, CGM, PFA, PFR) copy the files from there; none signs its own copy.

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceRoot = Split-Path -Parent $ScriptRoot
$Library = Join-Path $SourceRoot "src\ErkS.Updates\ErkS.Updates.csproj"
$Applier = Join-Path $SourceRoot "src\ErkS.Updates.Apply\ErkS.Updates.Apply.csproj"
$TimestampUrl = "http://timestamp.digicert.com"

[xml]$Project = Get-Content -Raw -LiteralPath $Library
$Version = ($Project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "ErkS.Updates.csproj has no <Version>."
}

$Work = Join-Path ([IO.Path]::GetTempPath()) ("erks-updates-" + [guid]::NewGuid().ToString("N"))
$Stage = Join-Path $Work "package"
New-Item -ItemType Directory -Path $Stage -Force | Out-Null
try {
    foreach ($Framework in @("net48", "net8.0", "net10.0")) {
        & dotnet publish $Library -c Release -f $Framework --nologo -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false `
            --artifacts-path (Join-Path $Work "artifacts-$Framework") -o (Join-Path $Stage $Framework)
        if ($LASTEXITCODE -ne 0) { throw "ErkS.Updates ($Framework) publish failed." }
    }
    & dotnet publish $Applier -c Release --nologo -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false `
        --artifacts-path (Join-Path $Work "artifacts-updater") -o (Join-Path $Stage "updater")
    if ($LASTEXITCODE -ne 0) { throw "ErkS.Updates.Apply publish failed." }
    # Not -Include: with -LiteralPath, Windows PowerShell 5.1 ignores it and returns every file.
    Get-ChildItem -LiteralPath $Stage -Recurse -File | Where-Object { $_.Extension -in ".pdb", ".xml" } | Remove-Item -Force

    function Get-Sha([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
    $Files = Get-ChildItem -LiteralPath $Stage -Recurse -File | Sort-Object FullName
    $Unsigned = @{}
    foreach ($File in $Files) {
        $Unsigned[$File.FullName.Substring($Stage.Length + 1).Replace('\', '/')] = Get-Sha $File.FullName
    }

    $Target = Join-Path $OutputRoot $Version
    $HashesPath = Join-Path $Target "hashes.json"
    if (Test-Path -LiteralPath $HashesPath) {
        $Known = Get-Content -Raw -LiteralPath $HashesPath | ConvertFrom-Json
        $Same = $true
        foreach ($Entry in $Known.files) {
            if ($Unsigned[$Entry.path] -ne $Entry.unsignedSha256) { $Same = $false }
        }
        if ($Same -and @($Known.files).Count -eq $Unsigned.Count) {
            Write-Host "ErkS.Updates $Version is already published and unchanged: $Target"
            return
        }
        throw "ErkS.Updates $Version is already published with other bytes. Bump <Version> in ErkS.Updates.csproj (and the applier)."
    }

    $Thumbprint = ($CodeSigningThumbprint -replace '\s', '').ToUpperInvariant()
    $Certificate = @(Get-ChildItem -Path Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue) |
        Where-Object { $_.Thumbprint -eq $Thumbprint -and $_.HasPrivateKey } | Select-Object -First 1
    if (-not $Certificate) { throw "Code-signing certificate '$Thumbprint' with a private key was not found." }
    foreach ($File in $Files | Where-Object { $_.Name -like "ErkS.*" -and $_.Extension -in ".dll", ".exe" }) {
        $Result = Set-AuthenticodeSignature -LiteralPath $File.FullName -Certificate $Certificate -HashAlgorithm SHA256 -TimestampServer $TimestampUrl
        if ($Result.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Signing failed for $($File.FullName): $($Result.StatusMessage)"
        }
    }

    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    Copy-Item -Path (Join-Path $Stage "*") -Destination $Target -Recurse -Force
    $Entries = foreach ($File in Get-ChildItem -LiteralPath $Target -Recurse -File | Where-Object { $_.Name -ne "hashes.json" } | Sort-Object FullName) {
        $Relative = $File.FullName.Substring($Target.Length + 1).Replace('\', '/')
        [ordered]@{ path = $Relative; unsignedSha256 = $Unsigned[$Relative]; sha256 = Get-Sha $File.FullName; size = $File.Length }
    }
    [ordered]@{ library = "ErkS.Updates"; version = $Version; signer = $Thumbprint; files = @($Entries) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $HashesPath -Encoding utf8
    Write-Host "ErkS.Updates $Version signed and published: $Target"
}
finally {
    Remove-Item -LiteralPath $Work -Recurse -Force -ErrorAction SilentlyContinue
}
