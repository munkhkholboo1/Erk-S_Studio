[CmdletBinding()]
param(
    [string]$InstallRoot = "",
    [switch]$NoLaunch,
    [switch]$SkipShortcuts,
    [switch]$SkipRegistration
)

$ErrorActionPreference = "Stop"
$ProductName = "Erk-S Studio"
$DisplayVersion = "Demo V0.001"
$DefaultInstallRoot = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Programs\Erk-S Studio"

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = if ([string]::IsNullOrWhiteSpace($env:ERKS_STUDIO_INSTALL_ROOT)) {
        $DefaultInstallRoot
    } else {
        $env:ERKS_STUDIO_INSTALL_ROOT
    }
}
if ($env:ERKS_STUDIO_NO_LAUNCH -eq "1") { $NoLaunch = $true }
if ($env:ERKS_STUDIO_SKIP_SHORTCUTS -eq "1") { $SkipShortcuts = $true }
if ($env:ERKS_STUDIO_SKIP_REGISTRATION -eq "1") { $SkipRegistration = $true }

$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$PayloadZip = Join-Path $PSScriptRoot "payload.zip"
if (-not (Test-Path -LiteralPath $PayloadZip -PathType Leaf)) {
    throw "Erk-S Studio payload.zip олдсонгүй."
}

$WorkRoot = Join-Path ([IO.Path]::GetTempPath()) ("ErkS-Studio-Install-" + [Guid]::NewGuid().ToString("N"))
$PayloadRoot = Join-Path $WorkRoot "payload"

try {
    New-Item -ItemType Directory -Path $PayloadRoot -Force | Out-Null
    Expand-Archive -LiteralPath $PayloadZip -DestinationPath $PayloadRoot -Force

    $SourceExe = Join-Path $PayloadRoot "ErkS.Studio.exe"
    if (-not (Test-Path -LiteralPath $SourceExe -PathType Leaf)) {
        throw "ErkS.Studio.exe багцад алга байна."
    }

    $ForbiddenFiles = Get-ChildItem -LiteralPath $PayloadRoot -Recurse -File | Where-Object {
        $_.Name -like "*.devroot" -or
        $_.Name -like "*.static" -or
        $_.Extension -in ".erksproject", ".erksalbum", ".rvt", ".dwg"
    }
    if ($ForbiddenFiles) {
        throw "Product payload хөгжүүлэлтийн marker эсвэл төслийн өгөгдөл агуулж байна."
    }

    $Running = Get-Process -Name "ErkS.Studio" -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and [IO.Path]::GetFullPath($_.Path).StartsWith(
                $InstallRoot.TrimEnd('\') + '\',
                [StringComparison]::OrdinalIgnoreCase)
        } catch {
            $false
        }
    }
    if ($Running) {
        $Running | Wait-Process -Timeout 45 -ErrorAction SilentlyContinue
    }

    $StillRunning = Get-Process -Name "ErkS.Studio" -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and [IO.Path]::GetFullPath($_.Path).StartsWith(
                $InstallRoot.TrimEnd('\') + '\',
                [StringComparison]::OrdinalIgnoreCase)
        } catch {
            $false
        }
    }
    if ($StillRunning) {
        throw "Erk-S Studio ажиллаж байна. Программыг хаагаад installer-ийг дахин ажиллуулна уу."
    }

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    $LegacyAppDirectory = [IO.Path]::GetFullPath((Join-Path $InstallRoot "app"))
    $ExpectedLegacyAppDirectory = $InstallRoot.TrimEnd('\') + "\app"
    if ($LegacyAppDirectory.Equals($ExpectedLegacyAppDirectory, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $LegacyAppDirectory -PathType Container)) {
        Remove-Item -LiteralPath $LegacyAppDirectory -Recurse -Force
    }

    Get-ChildItem -LiteralPath $PayloadRoot -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $InstallRoot -Recurse -Force
    }
    foreach ($Marker in "ErkS.Studio.devroot", "ErkS.Studio.static") {
        $MarkerPath = Join-Path $InstallRoot $Marker
        if (Test-Path -LiteralPath $MarkerPath -PathType Leaf) {
            Remove-Item -LiteralPath $MarkerPath -Force
        }
    }

    $InstalledExe = Join-Path $InstallRoot "ErkS.Studio.exe"
    if (-not (Test-Path -LiteralPath $InstalledExe -PathType Leaf)) {
        throw "Erk-S Studio суулгалт баталгаажаагүй."
    }

    if (-not $SkipShortcuts) {
        $Shell = New-Object -ComObject WScript.Shell
        $StartMenuDirectory = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs\Erk-S Studio"
        New-Item -ItemType Directory -Path $StartMenuDirectory -Force | Out-Null
        $StartShortcut = $Shell.CreateShortcut((Join-Path $StartMenuDirectory "Erk-S Studio.lnk"))
        $StartShortcut.TargetPath = $InstalledExe
        $StartShortcut.WorkingDirectory = $InstallRoot
        $StartShortcut.IconLocation = "$InstalledExe,0"
        $StartShortcut.Save()

        $DesktopShortcut = $Shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "Erk-S Studio.lnk"))
        $DesktopShortcut.TargetPath = $InstalledExe
        $DesktopShortcut.WorkingDirectory = $InstallRoot
        $DesktopShortcut.IconLocation = "$InstalledExe,0"
        $DesktopShortcut.Save()
    }

    if (-not $SkipRegistration) {
        $UninstallScript = Join-Path $InstallRoot "Uninstall-ErkS-Studio.ps1"
        $UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Erk-S Studio"
        New-Item -Path $UninstallKey -Force | Out-Null
        New-ItemProperty -Path $UninstallKey -Name "DisplayName" -Value $ProductName -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $UninstallKey -Name "DisplayVersion" -Value $DisplayVersion -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $UninstallKey -Name "Publisher" -Value "Erk-S" -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $UninstallKey -Name "InstallLocation" -Value $InstallRoot -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $UninstallKey -Name "DisplayIcon" -Value $InstalledExe -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $UninstallKey -Name "URLInfoAbout" -Value "https://erk-s.mn/products/studio" -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $UninstallKey -Name "NoModify" -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $UninstallKey -Name "NoRepair" -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $UninstallKey -Name "UninstallString" -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $UninstallScript + '"') -PropertyType String -Force | Out-Null
    }

    if (-not $NoLaunch) {
        Start-Process -FilePath $InstalledExe -WorkingDirectory $InstallRoot
    }
} finally {
    if (Test-Path -LiteralPath $WorkRoot -PathType Container) {
        Remove-Item -LiteralPath $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
