$ErrorActionPreference = "Stop"
$studioRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$snapshot = Join-Path $studioRoot "src\contracts\cloud-era-v1.openapi.json"
$committed = Join-Path $studioRoot "src\src\ErkS.CloudEra.Client\Generated\CloudEraGeneratedClient.g.cs"
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ("erks-cloud-client-" + [Guid]::NewGuid().ToString("N") + ".cs")

if (-not (Test-Path -LiteralPath $snapshot -PathType Leaf)) {
    throw "Cloud ERA OpenAPI snapshot was not found: $snapshot"
}
if (-not (Test-Path -LiteralPath $committed -PathType Leaf)) {
    throw "Generated Cloud ERA client was not found: $committed"
}

Push-Location $studioRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "Local NSwag tool restore failed." }
    & dotnet tool run nswag -- openapi2csclient "/input:$snapshot" "/output:$temporary" "/classname:CloudEraGeneratedClient" "/namespace:ErkS.CloudEra.Client.Generated" "/GenerateClientInterfaces:true" "/InjectHttpClient:true" "/DisposeHttpClient:false" "/UseBaseUrl:false" "/JsonLibrary:SystemTextJson"
    if ($LASTEXITCODE -ne 0) { throw "Cloud ERA client regeneration failed." }

    [string]$expected = [System.IO.File]::ReadAllText($committed).Replace("`r`n", "`n")
    [string]$actual = [System.IO.File]::ReadAllText($temporary).Replace("`r`n", "`n")
    if (-not $expected.Equals($actual, [StringComparison]::Ordinal)) {
        throw "Generated Cloud ERA client is stale. Run src/scripts/Generate-CloudEraClient.ps1."
    }
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}

Write-Host "Cloud ERA generated client matches the committed OpenAPI snapshot."
