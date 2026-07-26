[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$ExpectedPublisher = "Erk-S LLC",
    [string]$ExpectedProductVersion = "",
    [string]$OutputPath = "",
    [switch]$AllowPrivateTrust
)

$ErrorActionPreference = "Stop"
$Path = [IO.Path]::GetFullPath($Path)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = "$Path.trust.json"
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$OutputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$Checks = [Collections.Generic.List[object]]::new()
$HasFailure = $false

function Add-ArtifactCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Passed,
        [Parameter(Mandatory = $true)][string]$Details
    )

    $Status = if ($Passed) { "PASS" } else { "FAIL" }
    $script:Checks.Add([ordered]@{
        name = $Name
        status = $Status
        details = $Details
    })
    if (-not $Passed) {
        $script:HasFailure = $true
        Write-Host "[FAIL] $Name - $Details" -ForegroundColor Red
    }
    else {
        Write-Host "[PASS] $Name - $Details" -ForegroundColor Green
    }
}

$Signature = $null
$Certificate = $null
$Publisher = ""
$ChainStatus = "not-evaluated"
$Hash = ""
$FileVersion = ""
$ProductVersion = ""

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Add-ArtifactCheck -Name "Artifact exists" -Passed $false -Details "File was not found: $Path"
}
else {
    Add-ArtifactCheck -Name "Artifact exists" -Passed $true -Details $Path

    $Bytes = [IO.File]::ReadAllBytes($Path)
    $IsPortableExecutable = $Bytes.Length -ge 256 -and $Bytes[0] -eq 0x4D -and $Bytes[1] -eq 0x5A
    Add-ArtifactCheck `
        -Name "Windows executable format" `
        -Passed $IsPortableExecutable `
        -Details $(if ($IsPortableExecutable) { "Valid MZ/PE executable." } else { "The artifact is not a valid Windows executable." })

    $Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    Add-ArtifactCheck -Name "SHA-256 digest" -Passed ($Hash.Length -eq 64) -Details $Hash

    $VersionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $FileVersion = $VersionInfo.FileVersion
    $ProductVersion = $VersionInfo.ProductVersion
    if (-not [string]::IsNullOrWhiteSpace($ExpectedProductVersion)) {
        $VersionMatches = $ProductVersion -like "*$ExpectedProductVersion*"
        Add-ArtifactCheck `
            -Name "Product version" `
            -Passed $VersionMatches `
            -Details "Expected '$ExpectedProductVersion'; received '$ProductVersion'."
    }

    $Signature = Get-AuthenticodeSignature -LiteralPath $Path
    $Certificate = $Signature.SignerCertificate
    $SignatureValid = $Signature.Status -eq [Management.Automation.SignatureStatus]::Valid
    Add-ArtifactCheck `
        -Name "Authenticode signature" `
        -Passed $SignatureValid `
        -Details "Status=$($Signature.Status); $($Signature.StatusMessage)"

    if ($Certificate) {
        $Publisher = $Certificate.GetNameInfo(
            [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
            $false)
        Add-ArtifactCheck `
            -Name "Publisher identity" `
            -Passed ($Publisher -eq $ExpectedPublisher) `
            -Details "Expected '$ExpectedPublisher'; received '$Publisher'."

        $Now = [DateTimeOffset]::Now
        $CertificateCurrent = $Certificate.NotBefore -le $Now.LocalDateTime -and
            $Certificate.NotAfter -ge $Now.LocalDateTime
        Add-ArtifactCheck `
            -Name "Certificate validity period" `
            -Passed $CertificateCurrent `
            -Details "$($Certificate.NotBefore.ToString('O')) to $($Certificate.NotAfter.ToString('O'))"

        $CodeSigningOid = "1.3.6.1.5.5.7.3.3"
        $HasCodeSigningEku = @(
            $Certificate.Extensions |
                Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
                ForEach-Object { $_.EnhancedKeyUsages } |
                Where-Object { $_.Value -eq $CodeSigningOid }
        ).Count -gt 0
        Add-ArtifactCheck `
            -Name "Code-signing EKU" `
            -Passed $HasCodeSigningEku `
            -Details $(if ($HasCodeSigningEku) { $CodeSigningOid } else { "Required OID $CodeSigningOid is missing." })

        $IsSelfSigned = $Certificate.Subject -eq $Certificate.Issuer
        $Chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
        try {
            $Chain.ChainPolicy.RevocationMode =
                [Security.Cryptography.X509Certificates.X509RevocationMode]::Online
            $Chain.ChainPolicy.RevocationFlag =
                [Security.Cryptography.X509Certificates.X509RevocationFlag]::ExcludeRoot
            $Chain.ChainPolicy.VerificationFlags =
                [Security.Cryptography.X509Certificates.X509VerificationFlags]::NoFlag
            $ChainValid = $Chain.Build($Certificate)
            $ChainStatus = @($Chain.ChainStatus | ForEach-Object {
                "$($_.Status):$($_.StatusInformation.Trim())"
            }) -join "; "
            if ([string]::IsNullOrWhiteSpace($ChainStatus)) {
                $ChainStatus = "No chain errors."
            }
        }
        finally {
            $Chain.Dispose()
        }

        $TrustAccepted = $ChainValid -or ($AllowPrivateTrust -and $IsSelfSigned)
        $TrustDetails = if ($ChainValid) {
            "Public certificate chain is trusted."
        }
        elseif ($AllowPrivateTrust -and $IsSelfSigned) {
            "Private self-signed trust explicitly allowed for the transitional demo channel. $ChainStatus"
        }
        else {
            "Certificate chain is not trusted. $ChainStatus"
        }
        Add-ArtifactCheck -Name "Certificate chain trust" -Passed $TrustAccepted -Details $TrustDetails
    }
    else {
        Add-ArtifactCheck -Name "Publisher identity" -Passed $false -Details "Signer certificate is missing."
        Add-ArtifactCheck -Name "Certificate validity period" -Passed $false -Details "Signer certificate is missing."
        Add-ArtifactCheck -Name "Code-signing EKU" -Passed $false -Details "Signer certificate is missing."
        Add-ArtifactCheck -Name "Certificate chain trust" -Passed $false -Details "Signer certificate is missing."
    }
}

$Passed = @($Checks | Where-Object { $_.status -eq "PASS" }).Count
$Failed = @($Checks | Where-Object { $_.status -eq "FAIL" }).Count
$Report = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    path = $Path
    sha256 = $Hash
    fileVersion = $FileVersion
    productVersion = $ProductVersion
    expectedPublisher = $ExpectedPublisher
    publisher = $Publisher
    signatureStatus = if ($Signature) { [string]$Signature.Status } else { "Missing" }
    certificateThumbprint = if ($Certificate) { $Certificate.Thumbprint } else { "" }
    certificateChain = $ChainStatus
    allowPrivateTrust = [bool]$AllowPrivateTrust
    summary = [ordered]@{
        total = $Checks.Count
        passed = $Passed
        failed = $Failed
    }
    checks = $Checks
}
$Report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

Write-Host "Release artifact trust report: $OutputPath"
Write-Host "PASS=$Passed FAIL=$Failed"
if ($HasFailure) {
    exit 1
}

