<#
.SYNOPSIS
    Builds the SysmonConfigPusher MSI installer.

.DESCRIPTION
    This script builds the frontend, publishes the backend as self-contained,
    and creates the MSI installer package.

.PARAMETER Version
    Version number for the installer (e.g., "2.0.0"). Defaults to "2.0.0".

.PARAMETER SkipFrontend
    Skip rebuilding the frontend (use existing build).

.PARAMETER SkipPublish
    Skip republishing the backend (use existing publish output).

.EXAMPLE
    .\build-msi.ps1

.EXAMPLE
    .\build-msi.ps1 -Version "2.1.0"
#>

param(
    [string]$Version = "2.0.0",
    [switch]$SkipFrontend,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "src\SysmonConfigPusher.Service\bin\Release\net8.0\win-x64\publish"
$InstallerDir = $PSScriptRoot
$OutputMsi = Join-Path $InstallerDir "SysmonConfigPusher-$Version.msi"

Write-Host "Building SysmonConfigPusher MSI Installer v$Version" -ForegroundColor Cyan
Write-Host "=" * 50

# WiX SDK is restored automatically via NuGet

# Build frontend
if (-not $SkipFrontend) {
    Write-Host ""
    Write-Host "Step 1: Building frontend..." -ForegroundColor Cyan
    Push-Location (Join-Path $RepoRoot "src\SysmonConfigPusher.Web")
    npm install
    if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm build failed" }
    Pop-Location
} else {
    Write-Host "Step 1: Skipping frontend build" -ForegroundColor Yellow
}

# Publish backend
if (-not $SkipPublish) {
    Write-Host ""
    Write-Host "Step 2: Publishing self-contained backend..." -ForegroundColor Cyan
    Push-Location $RepoRoot
    dotnet publish src/SysmonConfigPusher.Service -c Release -r win-x64 --self-contained -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Pop-Location
} else {
    Write-Host "Step 2: Skipping backend publish" -ForegroundColor Yellow
}

# Verify publish output exists
if (-not (Test-Path (Join-Path $PublishDir "SysmonConfigPusher.Service.exe"))) {
    throw "Publish output not found at: $PublishDir"
}

# Build MSI
Write-Host ""
Write-Host "Step 3: Building MSI installer..." -ForegroundColor Cyan
Push-Location $InstallerDir

# Update version in wxs file
$wxsContent = Get-Content "Package.wxs" -Raw
$wxsContent = $wxsContent -replace 'Version="[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+"', "Version=`"$Version.0`""
$wxsContent | Set-Content "Package.wxs" -NoNewline

# Update publish path in wixproj
$wixprojContent = Get-Content "SysmonConfigPusher.wixproj" -Raw
$wixprojContent = $wixprojContent -replace 'PublishDir=[^<]+', "PublishDir=$PublishDir\"
$wixprojContent | Set-Content "SysmonConfigPusher.wixproj" -NoNewline

dotnet build SysmonConfigPusher.wixproj -c Release
if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

# Move output to expected location
$builtMsi = Join-Path $InstallerDir "bin\Release\SysmonConfigPusher.msi"
Move-Item $builtMsi $OutputMsi -Force

Pop-Location

# Summary
Write-Host ""
Write-Host "=" * 50
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Output: $OutputMsi"
Write-Host "Size: $([math]::Round((Get-Item $OutputMsi).Length / 1MB, 2)) MB"
