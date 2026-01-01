<#
.SYNOPSIS
    Uninstalls the SysmonConfigPusher Agent.

.DESCRIPTION
    Stops and removes the agent service, and optionally removes all files.

.PARAMETER KeepFiles
    Don't remove the installation directory (keeps config and logs).

.EXAMPLE
    .\uninstall-agent.ps1

.EXAMPLE
    .\uninstall-agent.ps1 -KeepFiles
#>

param(
    [switch]$KeepFiles
)

$ErrorActionPreference = "Stop"
$ServiceName = "SysmonConfigPusherAgent"
$InstallPath = "C:\Program Files\SysmonConfigPusher\Agent"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SysmonConfigPusher Agent Uninstaller" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    exit 1
}

# Stop service if running
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq "Running") {
        Write-Host "Stopping service..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    Write-Host "Removing service..." -ForegroundColor Yellow
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service removed." -ForegroundColor Green
} else {
    Write-Host "Service not found (already uninstalled?)." -ForegroundColor Yellow
}

# Remove files
if (-not $KeepFiles) {
    if (Test-Path $InstallPath) {
        Write-Host "Removing installation directory..." -ForegroundColor Yellow
        Remove-Item -Path $InstallPath -Recurse -Force
        Write-Host "Files removed." -ForegroundColor Green
    }
} else {
    Write-Host "Keeping installation files at: $InstallPath" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Uninstallation Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
