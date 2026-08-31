[CmdletBinding()]
param(
    [Parameter()]
    [string] $SiteName = $env:WEBSITE_SITE_NAME,

    [Parameter()]
    [string] $TablePrefix = $env:LASI_TABLE_PREFIX,

    [Parameter()]
    [ValidateRange(1, 65535)]
    [int] $Port = 5080,

    [Parameter()]
    [switch] $BuildFrontend
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot

Remove-Item Env:AzureWebJobsStorage__accountName -ErrorAction SilentlyContinue

if ([string]::IsNullOrWhiteSpace($SiteName)) {
    $SiteName = Read-Host "Logic App site name (WEBSITE_SITE_NAME)"
}

if ([string]::IsNullOrWhiteSpace($SiteName)) {
    throw "WEBSITE_SITE_NAME is required."
}

$env:WEBSITE_SITE_NAME = $SiteName
$env:ASPNETCORE_ENVIRONMENT = "Development"

if ([string]::IsNullOrWhiteSpace($env:AzureWebJobsStorage)) {
    throw "Set the AzureWebJobsStorage environment variable before running this script."
}

if ([string]::IsNullOrWhiteSpace($TablePrefix)) {
    Remove-Item Env:LASI_TABLE_PREFIX -ErrorAction SilentlyContinue
}
else {
    $env:LASI_TABLE_PREFIX = $TablePrefix
}

if ($BuildFrontend) {
    Push-Location (Join-Path $projectRoot "web")
    try {
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed with exit code $LASTEXITCODE." }

        npm run build
        if ($LASTEXITCODE -ne 0) { throw "Frontend build failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Starting Logic App Storage Inspector for site '$SiteName'."
Write-Host "Open http://localhost:$Port"
if (-not [string]::IsNullOrWhiteSpace($env:LASI_TABLE_PREFIX)) {
    Write-Host "Using explicit table prefix '$($env:LASI_TABLE_PREFIX)'."
}

Push-Location $projectRoot
try {
    dotnet run --configuration Release --urls "http://localhost:$Port"
    if ($LASTEXITCODE -ne 0) { throw "dotnet run failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
