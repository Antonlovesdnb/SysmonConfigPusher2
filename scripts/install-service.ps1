#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs SysmonConfigPusher as a Windows Service.

.DESCRIPTION
    This script installs the SysmonConfigPusher application as a Windows Service
    that runs under the Local System account (or a specified service account).

.PARAMETER InstallPath
    The path where the application files are located.
    Defaults to the parent directory of this script.

.PARAMETER ServiceAccount
    The account under which the service should run.
    Defaults to "LocalSystem". Can also be "NetworkService" or a domain account.

.PARAMETER StartupType
    The startup type for the service: Automatic, Manual, or Disabled.
    Defaults to "Automatic".

.EXAMPLE
    .\install-service.ps1

.EXAMPLE
    .\install-service.ps1 -InstallPath "C:\Program Files\SysmonConfigPusher" -StartupType Manual
#>

param(
    [string]$InstallPath = (Split-Path -Parent $PSScriptRoot),
    [string]$ServiceAccount = "LocalSystem",
    [ValidateSet("Automatic", "Manual", "Disabled")]
    [string]$StartupType = "Automatic"
)

$ErrorActionPreference = "Stop"

$ServiceName = "SysmonConfigPusher"
$DisplayName = "Sysmon Config Pusher"
$Description = "Manages Sysmon configurations across Windows endpoints in an Active Directory environment."
$ExePath = Join-Path $InstallPath "src\SysmonConfigPusher.Service\bin\Release\net8.0\publish\SysmonConfigPusher.Service.exe"

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

# Check if executable exists
if (-not (Test-Path $ExePath)) {
    # Try alternate path (if running from published output directly)
    $ExePath = Join-Path $InstallPath "SysmonConfigPusher.Service.exe"
    if (-not (Test-Path $ExePath)) {
        Write-Error "Cannot find SysmonConfigPusher.Service.exe at expected locations."
        Write-Error "Please ensure the application has been published first:"
        Write-Error "  dotnet publish -c Release"
        exit 1
    }
}

Write-Host "Installing $ServiceName..." -ForegroundColor Cyan
Write-Host "  Executable: $ExePath"
Write-Host "  Service Account: $ServiceAccount"
Write-Host "  Startup Type: $StartupType"
Write-Host ""

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service already exists. Stopping and removing..." -ForegroundColor Yellow

    if ($existingService.Status -eq "Running") {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Creating service..." -ForegroundColor Cyan

$binPath = "`"$ExePath`""

if ($ServiceAccount -eq "LocalSystem") {
    New-Service -Name $ServiceName `
                -BinaryPathName $binPath `
                -DisplayName $DisplayName `
                -Description $Description `
                -StartupType $StartupType | Out-Null
} else {
    # For other accounts, prompt for password if it's a domain account
    if ($ServiceAccount -notmatch "^(LocalSystem|NetworkService|LocalService)$") {
        $cred = Get-Credential -UserName $ServiceAccount -Message "Enter credentials for service account"
        New-Service -Name $ServiceName `
                    -BinaryPathName $binPath `
                    -DisplayName $DisplayName `
                    -Description $Description `
                    -StartupType $StartupType `
                    -Credential $cred | Out-Null
    } else {
        New-Service -Name $ServiceName `
                    -BinaryPathName $binPath `
                    -DisplayName $DisplayName `
                    -Description $Description `
                    -StartupType $StartupType | Out-Null
    }
}

# Configure recovery options (restart on failure)
Write-Host "Configuring recovery options..." -ForegroundColor Cyan
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# Create data directory
$dataPath = Join-Path $env:ProgramData "SysmonConfigPusher"
if (-not (Test-Path $dataPath)) {
    Write-Host "Creating data directory: $dataPath" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
}

Write-Host ""
Write-Host "Service installed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Copy appsettings.Production.json to the application directory"
Write-Host "  2. Configure TLS certificate in appsettings.Production.json"
Write-Host "  3. Configure AD groups for authorization"
Write-Host "  4. Start the service: Start-Service $ServiceName"
Write-Host ""
Write-Host "Useful commands:" -ForegroundColor Cyan
Write-Host "  Start-Service $ServiceName"
Write-Host "  Stop-Service $ServiceName"
Write-Host "  Restart-Service $ServiceName"
Write-Host "  Get-Service $ServiceName"
