#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls the SysmonConfigPusher Windows Service.

.DESCRIPTION
    This script stops and removes the SysmonConfigPusher Windows Service.
    Optionally removes the data directory (database, logs, etc.).

.PARAMETER RemoveData
    If specified, removes the data directory at %ProgramData%\SysmonConfigPusher.
    WARNING: This will delete the database and all configuration data!

.EXAMPLE
    .\uninstall-service.ps1

.EXAMPLE
    .\uninstall-service.ps1 -RemoveData
#>

param(
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

$ServiceName = "SysmonConfigPusher"
$DataPath = Join-Path $env:ProgramData "SysmonConfigPusher"

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

Write-Host "Uninstalling $ServiceName..." -ForegroundColor Cyan
Write-Host ""

# Check if service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$ServiceName' is not installed." -ForegroundColor Yellow
} else {
    # Stop the service if running
    if ($service.Status -eq "Running") {
        Write-Host "Stopping service..." -ForegroundColor Cyan
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 3
    }

    # Remove the service
    Write-Host "Removing service..." -ForegroundColor Cyan
    sc.exe delete $ServiceName | Out-Null

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Service removed successfully." -ForegroundColor Green
    } else {
        Write-Warning "Failed to remove service. It may be marked for deletion after reboot."
    }
}

# Remove data directory if requested
if ($RemoveData) {
    Write-Host ""
    Write-Host "WARNING: You are about to delete all application data!" -ForegroundColor Red
    Write-Host "This includes the database, logs, and cached binaries." -ForegroundColor Red
    Write-Host "Data path: $DataPath" -ForegroundColor Yellow
    Write-Host ""

    $confirmation = Read-Host "Type 'DELETE' to confirm data removal"
    if ($confirmation -eq "DELETE") {
        if (Test-Path $DataPath) {
            Write-Host "Removing data directory..." -ForegroundColor Cyan
            Remove-Item -Path $DataPath -Recurse -Force
            Write-Host "Data directory removed." -ForegroundColor Green
        } else {
            Write-Host "Data directory does not exist." -ForegroundColor Yellow
        }
    } else {
        Write-Host "Data removal cancelled." -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "Note: Data directory preserved at: $DataPath" -ForegroundColor Cyan
    Write-Host "Use -RemoveData switch to remove application data." -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Uninstall complete." -ForegroundColor Green
