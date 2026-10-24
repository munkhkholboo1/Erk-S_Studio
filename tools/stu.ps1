<#
.SYNOPSIS
  Drive Erk-S Studio from outside - PowerShell or cmd - through its bridge.

.DESCRIPTION
  The single way agents operate Studio (owner's rule, 2026-10-20): no
  computer-use clicks. Each action calls the same function as the Studio
  button. Rules: docs/STU-BRIDGE.md.

.EXAMPLE
  powershell -File stu.ps1 list
  powershell -File stu.ps1 actions
  powershell -File stu.ps1 status
  powershell -File stu.ps1 run open-project -Arg "code=STUDIO-20260817-2244"
  powershell -File stu.ps1 run sync-preview
  powershell -File stu.ps1 run sync -Arg confirm=true
  powershell -File stu.ps1 run relink-source -Arg "source=<id>","path=D:\x.dwg"
#>
param(
    [Parameter(Position = 0)][ValidateSet('list', 'actions', 'status', 'notices', 'run')][string]$Command = 'status',
    [Parameter(Position = 1)][string]$Action = '',
    [string[]]$Arg = @(),
    [int]$StudioPid = 0,
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$prefix = 'ErkS.Studio.Bridge.'

function Get-StudioBridges {
    $bridges = @()
    foreach ($path in [System.IO.Directory]::GetFiles('\\.\pipe\')) {
        $name = [System.IO.Path]::GetFileName($path)
        if (-not $name.StartsWith($prefix)) { continue }
        $pidText = $name.Substring($prefix.Length)
        $pidValue = 0
        if (-not [int]::TryParse($pidText, [ref]$pidValue)) { continue }
        $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
        if ($null -eq $process) { continue }
        $bridges += [pscustomobject]@{ Pid = $pidValue; Pipe = $name; Started = $process.StartTime; Title = $process.MainWindowTitle }
    }
    return ,$bridges
}

function Fail([string]$reason, [string]$message) {
    $answer = [ordered]@{ ok = $false; reasonCode = $reason; message = $message }
    Write-Output ($answer | ConvertTo-Json -Depth 4)
    exit 1
}

$bridges = Get-StudioBridges
if ($Command -eq 'list') {
    Write-Output ($bridges | ConvertTo-Json -Depth 3)
    exit 0
}

if ($bridges.Count -eq 0) {
    Fail 'studio_not_running' 'Studio is not running with a bridge (start Studio first).'
}
if ($StudioPid -gt 0) {
    $target = $bridges | Where-Object { $_.Pid -eq $StudioPid } | Select-Object -First 1
    if ($null -eq $target) { Fail 'studio_not_found' "No Studio bridge for pid $StudioPid." }
}
elseif ($bridges.Count -gt 1) {
    Fail 'several_studios' ("Several Studios run; pass -StudioPid: " + (($bridges | ForEach-Object { $_.Pid }) -join ', '))
}
else {
    $target = $bridges[0]
}

if ($Command -ne 'run') { $Action = $Command }
if ([string]::IsNullOrWhiteSpace($Action)) { Fail 'bad_request' 'run needs an action name (see: stu.ps1 actions).' }

$arguments = [ordered]@{}
# Under "powershell -File" a list written as 'a=1','b=2' arrives as ONE string "a=1,b=2"
# (only -Command parses it as an array) - split it again where a new key=value starts.
$pairs = @($Arg | ForEach-Object { [regex]::Split($_, ',(?=\s*[A-Za-z][\w-]*=)') } | Where-Object { $_ -ne '' })
foreach ($pair in $pairs) {
    $pair = $pair.Trim()
    $index = $pair.IndexOf('=')
    if ($index -lt 1) { Fail 'bad_request' "Argument '$pair' is not key=value." }
    $arguments[$pair.Substring(0, $index).Trim()] = $pair.Substring($index + 1)
}
$request = [ordered]@{ id = [guid]::NewGuid().ToString('N'); action = $Action; args = $arguments }
$line = ($request | ConvertTo-Json -Depth 4 -Compress)

$client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $target.Pipe, [System.IO.Pipes.PipeDirection]::InOut)
try {
    $client.Connect(10000)
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $writer = New-Object System.IO.StreamWriter($client, $utf8, 4096, $true)
    $writer.AutoFlush = $true
    $reader = New-Object System.IO.StreamReader($client, $utf8, $true, 4096, $true)
    $writer.WriteLine($line)
    $pending = $reader.ReadLineAsync()
    if (-not $pending.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) {
        Fail 'timeout' "Studio did not answer within $TimeoutSeconds s (the action may still be running; check: stu.ps1 status)."
    }
    $response = $pending.Result
}
finally {
    $client.Dispose()
}

if ([string]::IsNullOrWhiteSpace($response)) { Fail 'no_answer' 'Studio closed the bridge without an answer.' }
$parsed = $response | ConvertFrom-Json
Write-Output ($parsed | ConvertTo-Json -Depth 12)
if ($parsed.ok) { exit 0 } else { exit 1 }
