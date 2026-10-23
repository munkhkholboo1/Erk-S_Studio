[CmdletBinding()]
param(
    [string]$InstallRoot = ""
)

$ErrorActionPreference = "Stop"
$DefaultInstallRoot = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Programs\Erk-S Studio"
if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = if ([string]::IsNullOrWhiteSpace($env:ERKS_STUDIO_INSTALL_ROOT)) {
        $DefaultInstallRoot
    } else {
        $env:ERKS_STUDIO_INSTALL_ROOT
    }
}

$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$LocalProgramsRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Programs"))
if (-not $InstallRoot.StartsWith($LocalProgramsRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and
    [string]::IsNullOrWhiteSpace($env:ERKS_STUDIO_INSTALL_ROOT)) {
    throw "Uninstall target зөвшөөрөгдсөн LocalAppData\Programs хавтсанд биш байна."
}

# Erk-S Sync (SYN): its own --unregister removes the sign-in start and the Explorer menu.
$SyncExe = Join-Path $InstallRoot "ErkS.Sync.exe"
if (Test-Path -LiteralPath $SyncExe -PathType Leaf) {
    try { & $SyncExe --unregister | Out-Null } catch { }
}
# Fallback when the exe is already gone: the same keys, removed by name.
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "ErkS.Sync" -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "HKCU:\Software\Classes\Directory\shell\ErkS.Sync" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "HKCU:\Software\Classes\Directory\Background\shell\ErkS.Sync" -Recurse -Force -ErrorAction SilentlyContinue

Get-Process -Name "ErkS.Studio", "ErkS.Sync" -ErrorAction SilentlyContinue | Where-Object {
    try {
        $_.Path -and [IO.Path]::GetFullPath($_.Path).StartsWith(
            $InstallRoot.TrimEnd('\') + '\',
            [StringComparison]::OrdinalIgnoreCase)
    } catch {
        $false
    }
} | Stop-Process -Force -ErrorAction SilentlyContinue

$StartMenuDirectory = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs\Erk-S Studio"
$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Erk-S Studio.lnk"
if (Test-Path -LiteralPath $StartMenuDirectory) {
    Remove-Item -LiteralPath $StartMenuDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $DesktopShortcut) {
    Remove-Item -LiteralPath $DesktopShortcut -Force
}
Remove-Item -LiteralPath "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Erk-S Studio" -Recurse -Force -ErrorAction SilentlyContinue

$CleanupScript = Join-Path ([IO.Path]::GetTempPath()) ("ErkS-Studio-Uninstall-" + [Guid]::NewGuid().ToString("N") + ".ps1")
$QuotedRoot = $InstallRoot.Replace("'", "''")
Set-Content -LiteralPath $CleanupScript -Encoding UTF8 -Value @"
Start-Sleep -Seconds 2
if (Test-Path -LiteralPath '$QuotedRoot') {
    Remove-Item -LiteralPath '$QuotedRoot' -Recurse -Force
}
Remove-Item -LiteralPath `$PSCommandPath -Force -ErrorAction SilentlyContinue
"@
Start-Process -FilePath "powershell.exe" -ArgumentList @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $CleanupScript
) -WindowStyle Hidden
