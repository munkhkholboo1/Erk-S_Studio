param(
    [string]$ServerRepo = "E:\Erk-S Platform\Erk-S-Server",
    [switch]$SkipServerBuild
)

$ErrorActionPreference = "Stop"
$studioRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$serverRoot = [System.IO.Path]::GetFullPath($ServerRepo)
$serverProject = Join-Path $serverRoot "src\ErkS.LicenseServer\ErkS.LicenseServer.csproj"
$serverSpec = Join-Path $serverRoot "src\ErkS.LicenseServer\openapi\ErkS.LicenseServer.json"
$snapshot = Join-Path $studioRoot "src\contracts\cloud-era-v1.openapi.json"
$generated = Join-Path $studioRoot "src\src\ErkS.CloudEra.Client\Generated\CloudEraGeneratedClient.g.cs"

function Remove-TrailingWhitespace {
    param([string]$Path)

    [string]$content = [System.IO.File]::ReadAllText($Path)
    [string]$normalized = [System.Text.RegularExpressions.Regex]::Replace($content, "[ \t]+(?=\r?\n|\z)", "")
    if (-not $content.Equals($normalized, [StringComparison]::Ordinal)) {
        $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
        [System.IO.File]::WriteAllText($Path, $normalized, $utf8NoBom)
    }
}

if (-not $SkipServerBuild) {
    & dotnet build $serverProject -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Cloud ERA server build/OpenAPI generation failed."
    }
}

if (-not (Test-Path -LiteralPath $serverSpec -PathType Leaf)) {
    throw "Generated server OpenAPI document was not found: $serverSpec"
}

New-Item -ItemType Directory -Path (Split-Path -Parent $snapshot) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $generated) -Force | Out-Null
Copy-Item -LiteralPath $serverSpec -Destination $snapshot -Force

Push-Location $studioRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "Local NSwag tool restore failed."
    }

    & dotnet tool run nswag -- openapi2csclient "/input:$snapshot" "/output:$generated" "/classname:CloudEraGeneratedClient" "/namespace:ErkS.CloudEra.Client.Generated" "/GenerateClientInterfaces:true" "/InjectHttpClient:true" "/DisposeHttpClient:false" "/UseBaseUrl:false" "/JsonLibrary:SystemTextJson"
    if ($LASTEXITCODE -ne 0) {
        throw "Cloud ERA Studio client generation failed."
    }

    Remove-TrailingWhitespace -Path $generated
}
finally {
    Pop-Location
}

Write-Host "Cloud ERA contract snapshot and generated Studio client updated."
