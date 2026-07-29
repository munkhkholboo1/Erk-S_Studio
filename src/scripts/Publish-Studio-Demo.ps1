[CmdletBinding()]
param(
    [string]$ReleaseVersion = "V0.001.27",
    [string]$AssemblyVersion = "0.0.1.27",
    [string]$OutputDirectory = "",
    [string]$CodeSigningThumbprint = $env:ERKS_CODE_SIGN_CERT_THUMBPRINT,
    [string]$ExpectedPublisher = "Erk-S LLC",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$AllowPrivateTrustDemoCertificate,
    [string]$PrivateTrustCertificateSha256 = "A8A0A7C1435FC0E63A39CB3D101D9A532E1736D83FCBB65246DCA5B485636D8A"
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceRoot = Split-Path -Parent $ScriptRoot
$ProductRoot = Split-Path -Parent $SourceRoot
$ProductBuildRoot = [IO.Path]::GetFullPath((Join-Path $ProductRoot "builds\product"))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $ProductBuildRoot ("Demo-" + $ReleaseVersion)
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.StartsWith($ProductBuildRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Product output нь $ProductBuildRoot дотор байх ёстой."
}

$PublishDirectory = Join-Path $OutputDirectory "publish"
$InstallerBuildDirectory = Join-Path $OutputDirectory "installer-build"
$BuildArtifactsDirectory = Join-Path $OutputDirectory "dotnet-artifacts"
$AcceptanceDirectory = Join-Path $OutputDirectory "acceptance"
$SetupPath = Join-Path $OutputDirectory ("ErkS_Studio_Demo_" + $ReleaseVersion + "_Setup.exe")
$PortableZip = Join-Path $OutputDirectory ("ErkS_Studio_Demo_" + $ReleaseVersion + "_Portable.zip")
$PayloadZip = Join-Path $InstallerBuildDirectory "payload.zip"
$ProjectPath = Join-Path $SourceRoot "src\ErkS.Studio\ErkS.Studio.csproj"
$ExternalAcceptanceScript = Join-Path $ScriptRoot "Test-Studio-ExternalAcceptance.ps1"
$ReleaseArtifactScript = Join-Path $ScriptRoot "Test-Studio-ReleaseArtifact.ps1"
$TestProjects = @(
    (Join-Path $SourceRoot "tests\ErkS.Platform.Core.Tests\ErkS.Platform.Core.Tests.csproj"),
    (Join-Path $SourceRoot "tests\ErkS.Studio.App.Tests\ErkS.Studio.App.Tests.csproj")
)
$InstallerRoot = Join-Path $ProductRoot "installer"
$InstallerSource = Join-Path $InstallerRoot "ErkS.Studio.Setup.cs"
$InstallerManifest = Join-Path $InstallerRoot "ErkS.Studio.Setup.manifest"
$GeneratedInstallerSource = Join-Path $InstallerBuildDirectory "ErkS.Studio.Setup.Release.cs"
$CscCandidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$CscExe = $CscCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

function Get-CodeSigningContext {
    param([string]$Thumbprint)

    $NormalizedThumbprint = ($Thumbprint -replace '\s', '').ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($NormalizedThumbprint)) {
        throw "Release signing certificate is required. Set ERKS_CODE_SIGN_CERT_THUMBPRINT or pass -CodeSigningThumbprint."
    }

    $Certificate = @(
        Get-ChildItem -Path Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue
    ) | Where-Object {
        $_.Thumbprint -eq $NormalizedThumbprint -and $_.HasPrivateKey
    } | Select-Object -First 1
    if (-not $Certificate) {
        throw "Code-signing certificate '$NormalizedThumbprint' with a private key was not found."
    }

    $Publisher = $Certificate.GetNameInfo(
        [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false)
    if ($Publisher -ne $ExpectedPublisher) {
        throw "Code-signing publisher '$Publisher' does not match required publisher '$ExpectedPublisher'."
    }
    if ($Certificate.PublicKey.Oid.Value -ne "1.2.840.113549.1.1.1") {
        throw "The Studio release certificate must use an RSA public key for Smart App Control compatibility."
    }

    $Sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $CertificateSha256 = ([BitConverter]::ToString($Sha256.ComputeHash($Certificate.RawData))).Replace('-', '')
    }
    finally {
        $Sha256.Dispose()
    }
    $SelfSigned = $Certificate.Subject -eq $Certificate.Issuer

    $WindowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $SignTool = Get-ChildItem -LiteralPath $WindowsKitsRoot -Filter "signtool.exe" -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -like "*\x64" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    return [pscustomobject]@{
        Certificate = $Certificate
        CertificateSha256 = $CertificateSha256
        SelfSigned = $SelfSigned
        SignTool = if ($SignTool) { $SignTool.FullName } else { $null }
        UseMachineStore = $Certificate.PSParentPath -like "*LocalMachine*"
    }
}

function Invoke-ErkSCodeSign {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$SigningContext
    )

    if ($SigningContext.SignTool) {
        $SignArguments = @("sign")
        if ($SigningContext.UseMachineStore) {
            $SignArguments += "/sm"
        }
        $SignArguments += @(
            "/sha1", $SigningContext.Certificate.Thumbprint,
            "/fd", "SHA256",
            "/tr", $TimestampUrl,
            "/td", "SHA256",
            "/v",
            $Path
        )
        & $SigningContext.SignTool @SignArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Authenticode signing failed for '$Path'. Exit code: $LASTEXITCODE"
        }

        & $SigningContext.SignTool verify /pa /all /v $Path
        if ($LASTEXITCODE -ne 0) {
            throw "Authenticode verification failed for '$Path'. Exit code: $LASTEXITCODE"
        }
    }
    else {
        $SignResult = Set-AuthenticodeSignature `
            -LiteralPath $Path `
            -Certificate $SigningContext.Certificate `
            -HashAlgorithm SHA256 `
            -TimestampServer $TimestampUrl
        if ($SignResult.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "PowerShell Authenticode signing failed for '$Path': $($SignResult.StatusMessage)"
        }
    }

    $Signature = Get-AuthenticodeSignature -LiteralPath $Path
    $Publisher = $Signature.SignerCertificate.GetNameInfo(
        [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false)
    if ($Signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $Publisher -ne $ExpectedPublisher) {
        throw "Release gate rejected '$Path': signature status '$($Signature.Status)', publisher '$Publisher'."
    }
}

function Invoke-ReleaseArtifactGate {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ReportName
    )

    $Arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $ReleaseArtifactScript,
        "-Path", $Path,
        "-ExpectedPublisher", $ExpectedPublisher,
        "-ExpectedProductVersion", "Demo $ReleaseVersion",
        "-OutputPath", (Join-Path $AcceptanceDirectory $ReportName)
    )
    if ($AllowPrivateTrustDemoCertificate) {
        $Arguments += "-AllowPrivateTrust"
    }

    & powershell @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Release artifact trust gate failed for '$Path'. Exit code: $LASTEXITCODE"
    }
}

function Assert-UiSmokeArtifacts {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $ManifestPath = Join-Path $Directory "manifest.json"
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "UI smoke manifest was not created: $ManifestPath"
    }

    $Manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $Scenarios = @($Manifest.scenarios)
    if ($Scenarios.Count -ne 3) {
        throw "UI smoke must contain exactly three DPI scenarios; received $($Scenarios.Count)."
    }

    foreach ($Scenario in $Scenarios) {
        $ImagePath = [string]$Scenario.outputPath
        if (-not (Test-Path -LiteralPath $ImagePath -PathType Leaf)) {
            throw "UI smoke image is missing for '$($Scenario.name)': $ImagePath"
        }
        if ((Get-Item -LiteralPath $ImagePath).Length -lt 10240) {
            throw "UI smoke image is unexpectedly small for '$($Scenario.name)': $ImagePath"
        }
        if ([int]$Scenario.distinctColorBuckets -lt 4 -or
            [int64]$Scenario.visiblePixels * 4 -lt [int64]$Scenario.sampledPixels) {
            throw "UI smoke image validation failed for '$($Scenario.name)'."
        }
    }
}

if ($AssemblyVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "AssemblyVersion нь 0.0.1 эсвэл 0.0.1.1 хэлбэртэй байна."
}

foreach ($TestProject in $TestProjects) {
    if (-not (Test-Path -LiteralPath $TestProject -PathType Leaf)) {
        throw "Release regression test project was not found: $TestProject"
    }

    Write-Host "Running product-mode regression tests: $([IO.Path]::GetFileNameWithoutExtension($TestProject))"
    & dotnet test $TestProject -c "Release" --nologo `
        "-p:StudioProductBuild=true" `
        "-p:StudioReleaseVersion=$AssemblyVersion" `
        "-p:StudioReleaseLabel=Demo $ReleaseVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "Studio release regression gate failed for '$TestProject'. Exit code: $LASTEXITCODE"
    }
}

$SigningContext = Get-CodeSigningContext -Thumbprint $CodeSigningThumbprint
if ($SigningContext.SelfSigned) {
    if (-not $AllowPrivateTrustDemoCertificate) {
        throw "Public Studio releases cannot use a self-signed certificate. Obtain a CA-trusted code-signing certificate or explicitly use -AllowPrivateTrustDemoCertificate for the transitional demo channel."
    }
    if (-not $SigningContext.CertificateSha256.Equals(
            ($PrivateTrustCertificateSha256 -replace '[^0-9A-Fa-f]', '').ToUpperInvariant(),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "The self-signed demo certificate does not match the Studio updater certificate pin."
    }
    Write-Warning "Building a private-trust transitional demo. Chrome and Smart App Control require a CA-trusted certificate or Microsoft Store distribution."
}

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $InstallerBuildDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $AcceptanceDirectory -Force | Out-Null

& powershell `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File $ExternalAcceptanceScript `
    -OutputPath (Join-Path $AcceptanceDirectory "external-hosts.json") `
    -RequireExternalRepositories `
    -RequireInstalledHosts
if ($LASTEXITCODE -ne 0) {
    throw "AutoCAD/Revit external acceptance gate failed. Exit code: $LASTEXITCODE"
}

$PublishArguments = @(
    "publish", $ProjectPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "--nologo",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:StudioProductBuild=true",
    "-p:StudioReleaseVersion=$AssemblyVersion",
    "-p:StudioReleaseLabel=Demo $ReleaseVersion",
    "--artifacts-path", $BuildArtifactsDirectory,
    "-o", $PublishDirectory
)
& dotnet @PublishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Erk-S Studio product publish амжилтгүй боллоо."
}
Remove-Item -LiteralPath $BuildArtifactsDirectory -Recurse -Force

Copy-Item -LiteralPath (Join-Path $InstallerRoot "Uninstall-ErkS-Studio.ps1") -Destination $PublishDirectory -Force
Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File -Filter "*.pdb" | Remove-Item -Force
Get-ChildItem -LiteralPath $PublishDirectory -File -Filter "Microsoft.Web.WebView2.*.xml" | Remove-Item -Force

$ForbiddenFiles = Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | Where-Object {
    $_.Name -like "*.devroot" -or
    $_.Name -like "*.static" -or
    $_.Extension -in ".erksproject", ".erksalbum", ".rvt", ".dwg"
}
if ($ForbiddenFiles) {
    throw "Release gate: product payload хөгжүүлэлтийн marker эсвэл төслийн өгөгдөл агуулж байна: $($ForbiddenFiles.FullName -join ', ')"
}

Add-Type -TypeDefinition @"
using System;
using System.IO;

public static class ErkSReleaseGateScanner
{
    public static bool Contains(string path, byte[] pattern)
    {
        if (pattern == null || pattern.Length == 0)
            return false;

        byte[] buffer = new byte[1024 * 1024 + pattern.Length];
        int carry = 0;
        using (FileStream stream = File.OpenRead(path))
        {
            while (true)
            {
                int read = stream.Read(buffer, carry, 1024 * 1024);
                if (read == 0)
                    return false;

                int total = carry + read;
                if (IndexOf(buffer, total, pattern) >= 0)
                    return true;

                carry = Math.Min(pattern.Length - 1, total);
                if (carry > 0)
                    Buffer.BlockCopy(buffer, total - carry, buffer, 0, carry);
            }
        }
    }

    private static int IndexOf(byte[] buffer, int length, byte[] pattern)
    {
        int last = length - pattern.Length;
        for (int index = 0; index <= last; index++)
        {
            if (buffer[index] != pattern[0])
                continue;

            int patternIndex = 1;
            while (patternIndex < pattern.Length && buffer[index + patternIndex] == pattern[patternIndex])
                patternIndex++;
            if (patternIndex == pattern.Length)
                return index;
        }

        return -1;
    }
}
"@

function Test-BytePattern {
    param([string]$Path, [byte[]]$Pattern)
    return [ErkSReleaseGateScanner]::Contains($Path, $Pattern)
}

$ForbiddenText = @(
    "ATD-SIM-2026-002",
    "customer.test@erks.local",
    "Cloud ERA туршилтын төсөл",
    "Erk-S зураг төслийн компани",
    "munkhkholboo@gmail.com",
    "E:\Erk-S Platform\Erk-S Studio"
)
foreach ($File in Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File) {
    foreach ($Text in $ForbiddenText) {
        $Utf8 = [Text.Encoding]::UTF8.GetBytes($Text)
        $Utf16 = [Text.Encoding]::Unicode.GetBytes($Text)
        if ((Test-BytePattern -Path $File.FullName -Pattern $Utf8) -or
            (Test-BytePattern -Path $File.FullName -Pattern $Utf16)) {
            throw "Release gate: '$Text' мөр $($File.FullName) дотор байна."
        }
    }
}

$PublishedExe = Join-Path $PublishDirectory "ErkS.Studio.exe"
if (-not (Test-Path -LiteralPath $PublishedExe -PathType Leaf)) {
    throw "Release gate: ErkS.Studio.exe үүссэнгүй."
}
$VersionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($PublishedExe)
if ($VersionInfo.ProductVersion -notlike "*Demo $ReleaseVersion*") {
    throw "Release gate: executable ProductVersion нь Demo $ReleaseVersion биш байна: $($VersionInfo.ProductVersion)"
}

Invoke-ErkSCodeSign -Path $PublishedExe -SigningContext $SigningContext
Invoke-ReleaseArtifactGate `
    -Path $PublishedExe `
    -ReportName "published-exe-trust.json"

Compress-Archive -Path (Join-Path $PublishDirectory "*") -DestinationPath $PortableZip -CompressionLevel Optimal
Copy-Item -LiteralPath $PortableZip -Destination $PayloadZip -Force

$IconPath = Join-Path $ProductRoot "src\src\ErkS.Studio\Assets\logo-erks.ico"
if ([string]::IsNullOrWhiteSpace($CscExe)) {
    throw ".NET Framework C# compiler was not found."
}
foreach ($RequiredPath in $InstallerSource, $InstallerManifest, $IconPath, $PayloadZip) {
    if (-not (Test-Path -LiteralPath $RequiredPath -PathType Leaf)) {
        throw "Installer build input was not found: $RequiredPath"
    }
}

$InstallerSourceText = Get-Content -LiteralPath $InstallerSource -Raw -Encoding UTF8
$InstallerSourceText = $InstallerSourceText.Replace('Demo V0.001', "Demo $ReleaseVersion")
$InstallerSourceText = [Text.RegularExpressions.Regex]::Replace(
    $InstallerSourceText,
    '\[assembly: AssemblyVersion\("[^"]+"\)\]',
    ('[assembly: AssemblyVersion("' + $AssemblyVersion + '")]'))
$InstallerSourceText = [Text.RegularExpressions.Regex]::Replace(
    $InstallerSourceText,
    '\[assembly: AssemblyFileVersion\("[^"]+"\)\]',
    ('[assembly: AssemblyFileVersion("' + $AssemblyVersion + '")]'))
$InstallerSourceText = [Text.RegularExpressions.Regex]::Replace(
    $InstallerSourceText,
    '\[assembly: AssemblyInformationalVersion\("[^"]+"\)\]',
    ('[assembly: AssemblyInformationalVersion("Demo ' + $ReleaseVersion + '")]'))
Set-Content -LiteralPath $GeneratedInstallerSource -Value $InstallerSourceText -Encoding UTF8

$FrameworkDirectory = Split-Path -Parent $CscExe
$References = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "Microsoft.CSharp.dll",
    "System.IO.Compression.dll"
) | ForEach-Object { Join-Path $FrameworkDirectory $_ }
foreach ($Reference in $References) {
    if (-not (Test-Path -LiteralPath $Reference -PathType Leaf)) {
        throw "Installer framework reference was not found: $Reference"
    }
}

$CompilerArguments = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/out:$SetupPath",
    "/win32icon:$IconPath",
    "/win32manifest:$InstallerManifest",
    "/resource:$PayloadZip,ErkS.Studio.Payload"
)
$CompilerArguments += $References | ForEach-Object { "/reference:$_" }
$CompilerArguments += $GeneratedInstallerSource
& $CscExe @CompilerArguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $SetupPath -PathType Leaf)) {
    throw "Erk-S Studio setup compilation failed. Exit code: $LASTEXITCODE"
}
$SetupVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($SetupPath)
if ($SetupVersion.ProductVersion -ne "Demo $ReleaseVersion") {
    throw "Release gate: setup ProductVersion нь Demo $ReleaseVersion биш байна: $($SetupVersion.ProductVersion)"
}

Invoke-ErkSCodeSign -Path $SetupPath -SigningContext $SigningContext
Invoke-ReleaseArtifactGate `
    -Path $SetupPath `
    -ReportName "setup-trust.json"

$SmokeInstallDirectory = [IO.Path]::GetFullPath((Join-Path $OutputDirectory "installer-smoke"))
$ExpectedSmokePrefix = $OutputDirectory.TrimEnd('\') + '\'
if (-not $SmokeInstallDirectory.StartsWith($ExpectedSmokePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer smoke-test directory is outside the product output directory."
}
if (Test-Path -LiteralPath $SmokeInstallDirectory -PathType Container) {
    Remove-Item -LiteralPath $SmokeInstallDirectory -Recurse -Force
}

$PreviousInstallerEnvironment = @{
    ERKS_STUDIO_INSTALL_ROOT = $env:ERKS_STUDIO_INSTALL_ROOT
    ERKS_STUDIO_NO_LAUNCH = $env:ERKS_STUDIO_NO_LAUNCH
    ERKS_STUDIO_SKIP_SHORTCUTS = $env:ERKS_STUDIO_SKIP_SHORTCUTS
    ERKS_STUDIO_SKIP_REGISTRATION = $env:ERKS_STUDIO_SKIP_REGISTRATION
}
try {
    $env:ERKS_STUDIO_INSTALL_ROOT = $SmokeInstallDirectory
    $env:ERKS_STUDIO_NO_LAUNCH = "1"
    $env:ERKS_STUDIO_SKIP_SHORTCUTS = "1"
    $env:ERKS_STUDIO_SKIP_REGISTRATION = "1"
    $SmokeProcess = Start-Process -FilePath $SetupPath -ArgumentList "/quiet" -PassThru -Wait
} finally {
    foreach ($EnvironmentName in $PreviousInstallerEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($EnvironmentName, $PreviousInstallerEnvironment[$EnvironmentName], "Process")
    }
}
if ($SmokeProcess.ExitCode -ne 0) {
    $InstallerLog = Join-Path ([IO.Path]::GetTempPath()) "ErkS-Studio-Setup.log"
    $InstallerLogTail = if (Test-Path -LiteralPath $InstallerLog -PathType Leaf) {
        (Get-Content -LiteralPath $InstallerLog -Tail 30) -join [Environment]::NewLine
    } else {
        "No installer log was created."
    }
    throw "Final setup smoke test failed with exit code $($SmokeProcess.ExitCode).`n$InstallerLogTail"
}

$SmokeExe = Join-Path $SmokeInstallDirectory "ErkS.Studio.exe"
if (-not (Test-Path -LiteralPath $SmokeExe -PathType Leaf)) {
    throw "Final setup smoke test did not install ErkS.Studio.exe."
}
$SmokeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($SmokeExe)
if ($SmokeVersion.ProductVersion -notlike "*Demo $ReleaseVersion*") {
    throw "Final setup smoke test installed the wrong product version: $($SmokeVersion.ProductVersion)"
}
$SmokeForbiddenFiles = Get-ChildItem -LiteralPath $SmokeInstallDirectory -Recurse -File | Where-Object {
    $_.Name -like "*.devroot" -or
    $_.Name -like "*.static" -or
    $_.Extension -in ".erksproject", ".erksalbum", ".rvt", ".dwg"
}
if ($SmokeForbiddenFiles) {
    throw "Final setup smoke test found forbidden product data: $($SmokeForbiddenFiles.FullName -join ', ')"
}
Invoke-ReleaseArtifactGate `
    -Path $SmokeExe `
    -ReportName "clean-install-exe-trust.json"

$StartupSmokeFailureLog = Join-Path $SmokeInstallDirectory "release-smoke-failure.log"
if (Test-Path -LiteralPath $StartupSmokeFailureLog -PathType Leaf) {
    Remove-Item -LiteralPath $StartupSmokeFailureLog -Force
}

$CleanInstallUiSmokeDirectory = Join-Path $AcceptanceDirectory "clean-install-ui"
$StartupSmokeArgumentLine = (
    '--release-smoke-test --release-smoke-output="' +
    $CleanInstallUiSmokeDirectory +
    '"')
$StartupSmokeProcess = Start-Process `
    -FilePath $SmokeExe `
    -ArgumentList $StartupSmokeArgumentLine `
    -PassThru `
    -Wait
if ($StartupSmokeProcess.ExitCode -ne 0) {
    $StartupSmokeDetails = if (Test-Path -LiteralPath $StartupSmokeFailureLog -PathType Leaf) {
        Get-Content -LiteralPath $StartupSmokeFailureLog -Raw
    } else {
        "The installed application did not create a failure log."
    }
    throw "Final installed application startup smoke test failed with exit code $($StartupSmokeProcess.ExitCode).`n$StartupSmokeDetails"
}
Assert-UiSmokeArtifacts -Directory $CleanInstallUiSmokeDirectory

$UpdateSmokeArgumentLine = (
    '/quiet /update /nolaunch /skipshortcuts /skipregistration /installroot="' +
    $SmokeInstallDirectory +
    '"')
$UpdateHoldReadyPath = Join-Path $AcceptanceDirectory "update-running.ready"
if (Test-Path -LiteralPath $UpdateHoldReadyPath -PathType Leaf) {
    Remove-Item -LiteralPath $UpdateHoldReadyPath -Force
}
$UpdateHoldArgumentLine = (
    '--release-update-hold-test --release-update-ready="' +
    $UpdateHoldReadyPath +
    '"')
$UpdateHoldProcess = $null
$UpdateSmokeProcess = $null
$UpdateStartedAt = [DateTimeOffset]::UtcNow
try {
    $UpdateHoldProcess = Start-Process `
        -FilePath $SmokeExe `
        -ArgumentList $UpdateHoldArgumentLine `
        -PassThru

    $UpdateHoldDeadline = [DateTimeOffset]::Now.AddSeconds(20)
    while (-not (Test-Path -LiteralPath $UpdateHoldReadyPath -PathType Leaf) -and
        -not $UpdateHoldProcess.HasExited -and
        [DateTimeOffset]::Now -lt $UpdateHoldDeadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $UpdateHoldReadyPath -PathType Leaf)) {
        throw "Installed Studio did not enter the running-update acceptance state."
    }

    $UpdateSmokeProcess = Start-Process `
        -FilePath $SetupPath `
        -ArgumentList $UpdateSmokeArgumentLine `
        -PassThru `
        -Wait
    if ($UpdateSmokeProcess.ExitCode -ne 0) {
        $InstallerLog = Join-Path ([IO.Path]::GetTempPath()) "ErkS-Studio-Setup.log"
        $InstallerLogTail = if (Test-Path -LiteralPath $InstallerLog -PathType Leaf) {
            (Get-Content -LiteralPath $InstallerLog -Tail 30) -join [Environment]::NewLine
        } else {
            "No installer log was created."
        }
        throw "Final setup update smoke test failed with exit code $($UpdateSmokeProcess.ExitCode).`n$InstallerLogTail"
    }

    if (-not $UpdateHoldProcess.WaitForExit(10000)) {
        throw "The signed updater completed without closing the running Studio process."
    }
}
finally {
    if ($UpdateHoldProcess -and -not $UpdateHoldProcess.HasExited) {
        $UpdateHoldProcess.Kill()
        $UpdateHoldProcess.WaitForExit(5000)
    }
}

$UpdateRunningAcceptance = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    installedExecutable = $SmokeExe
    runningProcessId = $UpdateHoldProcess.Id
    updaterExitCode = $UpdateSmokeProcess.ExitCode
    runningProcessClosedAutomatically = $UpdateHoldProcess.HasExited
    durationMs = [int]([DateTimeOffset]::UtcNow - $UpdateStartedAt).TotalMilliseconds
}
$UpdateRunningAcceptance |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $AcceptanceDirectory "running-update.json") -Encoding UTF8
Remove-Item -LiteralPath $UpdateHoldReadyPath -Force

$UpdatedSmokeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($SmokeExe)
if ($UpdatedSmokeVersion.ProductVersion -notlike "*Demo $ReleaseVersion*") {
    throw "Final setup update smoke test installed the wrong product version: $($UpdatedSmokeVersion.ProductVersion)"
}
Invoke-ReleaseArtifactGate `
    -Path $SmokeExe `
    -ReportName "updated-install-exe-trust.json"
if (Test-Path -LiteralPath $StartupSmokeFailureLog -PathType Leaf) {
    Remove-Item -LiteralPath $StartupSmokeFailureLog -Force
}
$UpdatedInstallUiSmokeDirectory = Join-Path $AcceptanceDirectory "updated-install-ui"
$UpdatedStartupSmokeArgumentLine = (
    '--release-smoke-test --release-smoke-output="' +
    $UpdatedInstallUiSmokeDirectory +
    '"')
$UpdatedStartupSmokeProcess = Start-Process `
    -FilePath $SmokeExe `
    -ArgumentList $UpdatedStartupSmokeArgumentLine `
    -PassThru `
    -Wait
if ($UpdatedStartupSmokeProcess.ExitCode -ne 0) {
    $UpdatedStartupSmokeDetails = if (Test-Path -LiteralPath $StartupSmokeFailureLog -PathType Leaf) {
        Get-Content -LiteralPath $StartupSmokeFailureLog -Raw
    } else {
        "The updated application did not create a failure log."
    }
    throw "Final updated application startup smoke test failed with exit code $($UpdatedStartupSmokeProcess.ExitCode).`n$UpdatedStartupSmokeDetails"
}
Assert-UiSmokeArtifacts -Directory $UpdatedInstallUiSmokeDirectory

Remove-Item -LiteralPath $SmokeInstallDirectory -Recurse -Force
Remove-Item -LiteralPath $InstallerBuildDirectory -Recurse -Force

if ($false) {

$SedPath = Join-Path $IExpressDirectory "ErkS-Studio-Demo.sed"
$IconPath = Join-Path $ProductRoot "src\src\ErkS.Studio\Assets\logo-erks.ico"
$SedContent = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$SetupPath
FriendlyName=Erk-S Studio Demo $ReleaseVersion
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File Install-ErkS-Studio.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0="payload.zip"
FILE1="Install-ErkS-Studio.ps1"
[SourceFiles]
SourceFiles0=$IExpressDirectory\
[SourceFiles0]
%FILE0%=
%FILE1%=
"@
Set-Content -LiteralPath $SedPath -Value $SedContent -Encoding ASCII

if (-not (Test-Path -LiteralPath $IExpressExe -PathType Leaf)) {
    throw "IExpress Windows component олдсонгүй."
}
$PreviousLocation = Get-Location
try {
    Set-Location -LiteralPath $IExpressDirectory
    & $IExpressExe /N ([IO.Path]::GetFileName($SedPath))
} finally {
    Set-Location -LiteralPath $PreviousLocation
}
$IExpressDeadline = [DateTimeOffset]::Now.AddMinutes(8)
do {
    $PackagingProcesses = Get-Process -Name "iexpress", "makecab" -ErrorAction SilentlyContinue
    if (-not $PackagingProcesses) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::Now -lt $IExpressDeadline)
if ($PackagingProcesses) {
    throw "IExpress package build 8 минутын дотор дууссангүй."
}
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $SetupPath -PathType Leaf)) {
    throw "Erk-S Studio setup үүссэнгүй. IExpress exit code: $LASTEXITCODE"
}

}

$SetupHash = (Get-FileHash -LiteralPath $SetupPath -Algorithm SHA256).Hash
$ReleaseMetadata = [ordered]@{
    productCode = "ErkS.Studio"
    edition = "Demo"
    version = $ReleaseVersion
    displayVersion = "Demo $ReleaseVersion"
    setupFile = [IO.Path]::GetFileName($SetupPath)
    sha256 = $SetupHash
    sizeBytes = (Get-Item -LiteralPath $SetupPath).Length
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    authenticodeSigned = $true
    publisher = $ExpectedPublisher
    signingCertificateThumbprint = $SigningContext.Certificate.Thumbprint
    signingCertificateSha256 = $SigningContext.CertificateSha256
    signingTrust = if ($SigningContext.SelfSigned) { "private-pinned-demo" } else { "public-ca" }
    publicDistributionReady = -not $SigningContext.SelfSigned
    timestampUrl = $TimestampUrl
    productDataIncluded = $false
    devUpdateIncluded = $false
    acceptanceReports = [ordered]@{
        externalHosts = "acceptance/external-hosts.json"
        publishedExecutableTrust = "acceptance/published-exe-trust.json"
        setupTrust = "acceptance/setup-trust.json"
        cleanInstallExecutableTrust = "acceptance/clean-install-exe-trust.json"
        updatedInstallExecutableTrust = "acceptance/updated-install-exe-trust.json"
        runningApplicationUpdate = "acceptance/running-update.json"
        cleanInstallUi = "acceptance/clean-install-ui/manifest.json"
        updatedInstallUi = "acceptance/updated-install-ui/manifest.json"
    }
}
$ReleaseMetadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory "release.json") -Encoding UTF8

Write-Host "Erk-S Studio Demo $ReleaseVersion product package ready."
Write-Host "Setup: $SetupPath"
Write-Host "SHA256: $SetupHash"
