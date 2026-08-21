# Builds the Studio app module and stages it where the DevMod host loads it.
#
# The host is a thin shell: it shadow-copies ErkS.Studio.App.dll and its
# dependencies out of <repo>\builds\devmod\app on every reload. Only the module
# needs rebuilding for a UI or platform change, so this is the whole dev loop -
# run it, then reload in the running host instead of publishing a release.
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceRoot = Split-Path -Parent $ScriptRoot
$RepoRoot = Split-Path -Parent $SourceRoot
$AppProject = Join-Path $SourceRoot "src\ErkS.Studio.App\ErkS.Studio.App.csproj"
$DevModApp = Join-Path $RepoRoot "builds\devmod\app"

if (-not (Test-Path -LiteralPath $AppProject -PathType Leaf)) {
    throw "App module project not found: $AppProject"
}

if (-not $SkipTests) {
    Write-Host "1/3 Running tests..."
    & dotnet test (Join-Path $SourceRoot "ErkS.Studio.slnx") -v q --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed. The DevMod module was not staged."
    }
} else {
    Write-Host "1/3 Tests skipped."
}

Write-Host "2/3 Building the app module..."
# The module carries ErkS.Platform.Core and ErkS.Platform.Pdf with it. An
# incremental build of the app project alone can leave those at an older
# revision in the output folder, which stages a module whose platform layer is
# behind its UI - the fix appears to do nothing at all.
& dotnet build $AppProject -c $Configuration -v q --nologo --no-incremental
if ($LASTEXITCODE -ne 0) {
    throw "Build failed. The DevMod module was not staged."
}

$buildOutput = Get-ChildItem -Path (Join-Path $SourceRoot "src\ErkS.Studio.App\bin\$Configuration") -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "ErkS.Studio.App.dll") } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $buildOutput) {
    throw "Built module not found under bin\$Configuration."
}

Write-Host "3/3 Staging to $DevModApp"
New-Item -ItemType Directory -Force -Path $DevModApp | Out-Null
Copy-Item -Path (Join-Path $buildOutput.FullName "*") -Destination $DevModApp -Recurse -Force

# The platform assemblies are the ones a stale stage hides, so they are named
# here rather than left to be assumed current.
foreach ($name in @("ErkS.Studio.App", "ErkS.Platform.Core", "ErkS.Platform.Pdf")) {
    $staged = Get-Item -LiteralPath (Join-Path $DevModApp "$name.dll") -ErrorAction SilentlyContinue
    if ($null -eq $staged) {
        throw "Staged module is incomplete: $name.dll is missing from $DevModApp."
    }
    Write-Host ("  {0,-22} {1}" -f $name, $staged.LastWriteTime)
}
Write-Host "Reload in the running DevMod host to pick it up."
