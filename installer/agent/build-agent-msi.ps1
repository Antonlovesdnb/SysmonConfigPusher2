<#
.SYNOPSIS
    Builds the SysmonConfigPusher Agent MSI installer.

.PARAMETER Version
    Version number for the installer (e.g., "2.1.0"). Defaults to "2.1.0".

.PARAMETER SkipPublish
    Skip republishing the agent (use existing publish output).

.EXAMPLE
    .\build-agent-msi.ps1

.EXAMPLE
    .\build-agent-msi.ps1 -Version "2.2.0"
#>

param(
    [string]$Version = "2.1.0",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$AgentProject = Join-Path $RepoRoot "src\SysmonConfigPusher.Agent\SysmonConfigPusher.Agent.csproj"
$PublishDir = Join-Path $RepoRoot "src\SysmonConfigPusher.Agent\bin\Release\net8.0-windows\win-x64\publish"
$OutputMsi = Join-Path $ScriptDir "SysmonConfigPusherAgent-$Version.msi"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Building SysmonConfigPusher Agent MSI" -ForegroundColor Cyan
Write-Host " Version: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Publish agent
if (-not $SkipPublish) {
    Write-Host "Step 1: Publishing agent..." -ForegroundColor Yellow
    Push-Location $RepoRoot
    dotnet publish $AgentProject -c Release -r win-x64 --self-contained -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Pop-Location
} else {
    Write-Host "Step 1: Skipping agent publish" -ForegroundColor Yellow
}

# Verify publish output
if (-not (Test-Path (Join-Path $PublishDir "SysmonConfigPusher.Agent.exe"))) {
    throw "Agent executable not found at: $PublishDir"
}

# Update version in wxs file
Write-Host "Step 2: Updating version..." -ForegroundColor Yellow
$wxsPath = Join-Path $ScriptDir "Package.wxs"
$wxsContent = Get-Content $wxsPath -Raw
$wxsContent = $wxsContent -replace 'Version="[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+"', "Version=`"$Version.0`""
$wxsContent | Set-Content $wxsPath -NoNewline

# Build MSI
Write-Host "Step 3: Building MSI..." -ForegroundColor Yellow
Push-Location $ScriptDir

dotnet build SysmonConfigPusherAgent.wixproj -c Release
if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

# Move output
$builtMsi = Join-Path $ScriptDir "bin\Release\SysmonConfigPusherAgent.msi"
if (Test-Path $builtMsi) {
    Move-Item $builtMsi $OutputMsi -Force
}

Pop-Location

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Output: $OutputMsi" -ForegroundColor Cyan
if (Test-Path $OutputMsi) {
    Write-Host "Size:   $([math]::Round((Get-Item $OutputMsi).Length / 1MB, 2)) MB" -ForegroundColor Cyan
}
Write-Host ""
Write-Host "Installation (GUI):" -ForegroundColor White
Write-Host "  msiexec /i `"$OutputMsi`"" -ForegroundColor Gray
Write-Host ""
Write-Host "Installation (Silent):" -ForegroundColor White
Write-Host "  msiexec /i `"$OutputMsi`" /qn SERVER_URL=https://server:5001 REGISTRATION_TOKEN=yourtoken SKIP_CERT_VALIDATION=1" -ForegroundColor Gray
