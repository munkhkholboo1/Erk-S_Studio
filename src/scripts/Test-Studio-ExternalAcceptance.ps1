[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [string]$AutoCADRoot = "",
    [string]$RevitRoot = "",
    [switch]$RequireExternalRepositories,
    [switch]$RequireInstalledHosts
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceRoot = Split-Path -Parent $ScriptRoot
$ProductRoot = Split-Path -Parent $SourceRoot
$WorkspaceRoot = Split-Path -Parent $ProductRoot

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $ProductRoot "artifacts\external-acceptance.json"
}
if ([string]::IsNullOrWhiteSpace($AutoCADRoot)) {
    $AutoCADRoot = Join-Path $WorkspaceRoot "Erk-S Platform For Autocad\src\autocad\2026-dev\source\AutoCAD_v2"
}
if ([string]::IsNullOrWhiteSpace($RevitRoot)) {
    $RevitRoot = Join-Path $WorkspaceRoot "Erk-S Platform For Revit\src\revit\2026-dev\source"
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$AutoCADRoot = [IO.Path]::GetFullPath($AutoCADRoot)
$RevitRoot = [IO.Path]::GetFullPath($RevitRoot)
$OutputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$Checks = [Collections.Generic.List[object]]::new()
$HasFailure = $false

function Get-OutputSummary {
    param(
        [AllowEmptyString()][string]$Text,
        [int]$MaximumCharacters = 4000
    )

    $Normalized = ([string]$Text).Trim()
    if ($Normalized.Length -le $MaximumCharacters) {
        return $Normalized
    }

    return "... output truncated ...$([Environment]::NewLine)" +
        $Normalized.Substring($Normalized.Length - $MaximumCharacters)
}

function Add-AcceptanceResult {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet("PASS", "FAIL", "SKIPPED")][string]$Status,
        [Parameter(Mandatory = $true)][string]$Details,
        [long]$DurationMs = 0
    )

    $Details = Get-OutputSummary -Text $Details
    $script:Checks.Add([ordered]@{
        name = $Name
        status = $Status
        details = $Details
        durationMs = $DurationMs
    })
    if ($Status -eq "FAIL") {
        $script:HasFailure = $true
        Write-Host "[FAIL] $Name - $Details" -ForegroundColor Red
    }
    elseif ($Status -eq "SKIPPED") {
        Write-Host "[SKIPPED] $Name - $Details" -ForegroundColor Yellow
    }
    else {
        Write-Host "[PASS] $Name - $Details" -ForegroundColor Green
    }
}

function Invoke-AcceptanceCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $Timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        $Details = & $Action
        if ($Details -is [array]) {
            $Details = $Details -join [Environment]::NewLine
        }
        if ([string]::IsNullOrWhiteSpace([string]$Details)) {
            $Details = "Completed."
        }
        Add-AcceptanceResult -Name $Name -Status "PASS" -Details ([string]$Details).Trim() -DurationMs $Timer.ElapsedMilliseconds
    }
    catch {
        Add-AcceptanceResult -Name $Name -Status "FAIL" -Details $_.Exception.Message -DurationMs $Timer.ElapsedMilliseconds
    }
    finally {
        $Timer.Stop()
    }
}

# Called from the check scriptblocks, which Invoke-AcceptanceCheck runs in this
# script's own scope. Two of them used to be wrapped in GetNewClosure() to pin
# the loop's $Year; that puts the block in an isolated module scope where this
# function is not visible, and every host build failed with "not recognized"
# rather than with whatever the build actually said. The blocks run inside the
# same iteration that creates them, so $Year needs no pinning.
function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    Push-Location -LiteralPath $WorkingDirectory
    try {
        $ProcessOutput = (& $FilePath @Arguments 2>&1 | Out-String)
        $ExitCode = $LASTEXITCODE
        if ($ExitCode -ne 0) {
            $OutputSummary = Get-OutputSummary -Text $ProcessOutput
            throw "'$FilePath' failed with exit code $ExitCode.`n$OutputSummary"
        }
    }
    finally {
        Pop-Location
    }
}

function Test-RepositoryRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (Test-Path -LiteralPath $Path -PathType Container) {
        Add-AcceptanceResult -Name "$Name repository" -Status "PASS" -Details $Path
        return $true
    }

    $Status = if ($RequireExternalRepositories) { "FAIL" } else { "SKIPPED" }
    Add-AcceptanceResult -Name "$Name repository" -Status $Status -Details "Repository was not found: $Path"
    return $false
}

function Test-HostInstallation {
    param(
        [Parameter(Mandatory = $true)][string]$Product,
        [Parameter(Mandatory = $true)][string]$Year,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (Test-Path -LiteralPath $Path -PathType Container) {
        return $true
    }

    $Status = if ($RequireInstalledHosts) { "FAIL" } else { "SKIPPED" }
    Add-AcceptanceResult `
        -Name "$Product $Year host build" `
        -Status $Status `
        -Details "$Product $Year is not installed at '$Path'."
    return $false
}

$AutoCADRepositoryAvailable = Test-RepositoryRoot -Name "AutoCAD" -Path $AutoCADRoot
if ($AutoCADRepositoryAvailable) {
    $AutoCADTestProject = Join-Path $AutoCADRoot "tests\ErkS.AutoCAD.SheetPackages.Tests\ErkS.AutoCAD.SheetPackages.Tests.csproj"
    Invoke-AcceptanceCheck -Name "AutoCAD source package tests" -Action {
        if (-not (Test-Path -LiteralPath $AutoCADTestProject -PathType Leaf)) {
            throw "Test project was not found: $AutoCADTestProject"
        }
        Invoke-CheckedProcess `
            -FilePath "dotnet" `
            -Arguments @("test", $AutoCADTestProject, "-c", "Release", "--nologo") `
            -WorkingDirectory $AutoCADRoot
        return "AutoCAD source package tests passed."
    }

    $AutoCADManifest = Join-Path $AutoCADRoot "package\ErkS.AutoCAD.v2.bundle\PackageContents.xml"
    Invoke-AcceptanceCheck -Name "AutoCAD 2026/2027 bundle manifest" -Action {
        if (-not (Test-Path -LiteralPath $AutoCADManifest -PathType Leaf)) {
            throw "Bundle manifest was not found: $AutoCADManifest"
        }

        [xml]$Manifest = Get-Content -LiteralPath $AutoCADManifest -Raw
        $Package = $Manifest.ApplicationPackage
        if ($Package.ProductCode -ne "{EFD282B5-29EE-4B55-B588-F63412F6F8AE}") {
            throw "Unexpected AutoCAD bundle ProductCode '$($Package.ProductCode)'."
        }

        $Expected = @(
            @{
                Year = "2026"
                Series = "R25.1"
                Module = "./Contents/Windows/2026/ErkS.AutoCAD.Host.dll"
            },
            @{
                Year = "2027"
                Series = "R26.0"
                Module = "./Contents/Windows/2027/ErkS.AutoCAD.Host.dll"
            }
        )
        foreach ($Target in $Expected) {
            $Component = @($Package.Components) |
                Where-Object {
                    $_.RuntimeRequirements.SeriesMin -eq $Target.Series -and
                    $_.RuntimeRequirements.SeriesMax -eq $Target.Series
                } |
                Select-Object -First 1
            if (-not $Component) {
                throw "AutoCAD $($Target.Year) component '$($Target.Series)' is missing."
            }

            $Entry = $Component.ComponentEntry
            if ($Entry.ModuleName -ne $Target.Module) {
                throw "AutoCAD $($Target.Year) module path is '$($Entry.ModuleName)'."
            }
            if ($Entry.LoadOnAutoCADStartup -ne "True" -or
                $Entry.LoadOnCommandInvocation -ne "True") {
                throw "AutoCAD $($Target.Year) is not configured for startup and command loading."
            }
        }

        return "R25.1/AutoCAD 2026 and R26.0/AutoCAD 2027 entries are explicit and startup-enabled."
    }

    $AutoCADBuildScript = Join-Path $AutoCADRoot "tools\build_host.ps1"
    foreach ($Year in "2026", "2027") {
        $HostDirectory = "C:\Program Files\Autodesk\AutoCAD $Year"
        if (Test-HostInstallation -Product "AutoCAD" -Year $Year -Path $HostDirectory) {
            Invoke-AcceptanceCheck -Name "AutoCAD $Year host build" -Action {
                if (-not (Test-Path -LiteralPath $AutoCADBuildScript -PathType Leaf)) {
                    throw "Host build script was not found: $AutoCADBuildScript"
                }
                Invoke-CheckedProcess `
                    -FilePath "powershell" `
                    -Arguments @(
                        "-NoProfile",
                        "-ExecutionPolicy", "Bypass",
                        "-File", $AutoCADBuildScript,
                        "-Configuration", "Release",
                        "-AutoCADYear", $Year,
                        "-AcadInstallDir", $HostDirectory
                    ) `
                    -WorkingDirectory $AutoCADRoot
                return "Built against the installed AutoCAD $Year API."
            }
        }
    }
}

$RevitRepositoryAvailable = Test-RepositoryRoot -Name "Revit" -Path $RevitRoot
if ($RevitRepositoryAvailable) {
    $RevitTestProject = Join-Path $RevitRoot "Tests\ErkS.Revit.TitleBlocks.Tests\ErkS.Revit.TitleBlocks.Tests.csproj"
    Invoke-AcceptanceCheck -Name "Revit title block policy tests" -Action {
        if (-not (Test-Path -LiteralPath $RevitTestProject -PathType Leaf)) {
            throw "Test project was not found: $RevitTestProject"
        }
        Invoke-CheckedProcess `
            -FilePath "dotnet" `
            -Arguments @("test", $RevitTestProject, "-c", "Release", "--nologo") `
            -WorkingDirectory $RevitRoot
        return "Revit title block policy tests passed."
    }

    $RevitProject = Join-Path $RevitRoot "Revit\ErkS.Revit.TitleBlocks\ErkS.Revit.Platform.csproj"
    foreach ($Year in "2026", "2027") {
        $HostDirectory = "C:\Program Files\Autodesk\Revit $Year"
        if (Test-HostInstallation -Product "Revit" -Year $Year -Path $HostDirectory) {
            Invoke-AcceptanceCheck -Name "Revit $Year host build" -Action {
                if (-not (Test-Path -LiteralPath $RevitProject -PathType Leaf)) {
                    throw "Host project was not found: $RevitProject"
                }
                Invoke-CheckedProcess `
                    -FilePath "dotnet" `
                    -Arguments @(
                        "build", $RevitProject,
                        "-c", "Release",
                        "--nologo",
                        "-p:RevitVersion=$Year",
                        "-p:RevitInstallDir=$HostDirectory",
                        "-p:RegisterDevAddIn=false",
                        "-p:PackTitleBlockLibrary=false",
                        "-p:ErkSProductBuild=true"
                    ) `
                    -WorkingDirectory $RevitRoot
                return "Built against the installed Revit $Year API without registering a dev add-in."
            }
        }
    }
}

$Passed = @($Checks | Where-Object { $_.status -eq "PASS" }).Count
$Failed = @($Checks | Where-Object { $_.status -eq "FAIL" }).Count
$Skipped = @($Checks | Where-Object { $_.status -eq "SKIPPED" }).Count
$Report = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    studioRepository = $ProductRoot
    autoCADRepository = $AutoCADRoot
    revitRepository = $RevitRoot
    requirements = [ordered]@{
        externalRepositories = [bool]$RequireExternalRepositories
        installedHosts = [bool]$RequireInstalledHosts
    }
    summary = [ordered]@{
        total = $Checks.Count
        passed = $Passed
        failed = $Failed
        skipped = $Skipped
    }
    checks = $Checks
}
$Report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

Write-Host "External acceptance report: $OutputPath"
Write-Host "PASS=$Passed FAIL=$Failed SKIPPED=$Skipped"
if ($HasFailure) {
    exit 1
}
