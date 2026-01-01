<#
.SYNOPSIS
    Installs the SysmonConfigPusher Agent on a Windows machine.

.DESCRIPTION
    This script installs the SysmonConfigPusher Agent as a Windows service,
    configures it to connect to the central server, and starts the service.

.PARAMETER ServerUrl
    The URL of the SysmonConfigPusher server (e.g., https://server.domain.com:5001)

.PARAMETER RegistrationToken
    The registration token configured on the server.

.PARAMETER InstallPath
    Installation directory. Defaults to "C:\Program Files\SysmonConfigPusher\Agent"

.PARAMETER Tags
    Optional comma-separated tags for grouping (e.g., "cloud,azure,production")

.PARAMETER SkipCertValidation
    Skip TLS certificate validation (for testing with self-signed certs)

.PARAMETER CertThumbprint
    Optional certificate thumbprint for pinning (instead of SkipCertValidation)

.PARAMETER AgentSourcePath
    Path to the agent files. Defaults to the same directory as this script.

.EXAMPLE
    .\install-agent.ps1 -ServerUrl "https://sysmon-server:5001" -RegistrationToken "my-secret-token"

.EXAMPLE
    .\install-agent.ps1 -ServerUrl "https://sysmon-server:5001" -RegistrationToken "token" -Tags "azure,prod" -SkipCertValidation
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ServerUrl,

    [Parameter(Mandatory=$true)]
    [string]$RegistrationToken,

    [string]$InstallPath = "C:\Program Files\SysmonConfigPusher\Agent",

    [string]$Tags = "",

    [switch]$SkipCertValidation,

    [string]$CertThumbprint = "",

    [string]$AgentSourcePath = ""
)

$ErrorActionPreference = "Stop"
$ServiceName = "SysmonConfigPusherAgent"

# Determine source path
if ([string]::IsNullOrEmpty($AgentSourcePath)) {
    # First, check if the exe is in the same folder as this script
    $scriptDir = $PSScriptRoot
    if (Test-Path (Join-Path $scriptDir "SysmonConfigPusher.Agent.exe")) {
        $AgentSourcePath = $scriptDir
    } else {
        # Fall back to looking in installer\agent-output relative to repo root
        $AgentSourcePath = Split-Path -Parent $PSScriptRoot
        $AgentSourcePath = Join-Path $AgentSourcePath "installer\agent-output"
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SysmonConfigPusher Agent Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    exit 1
}

# Check if agent source exists
$agentExe = Join-Path $AgentSourcePath "SysmonConfigPusher.Agent.exe"
if (-not (Test-Path $agentExe)) {
    Write-Host "ERROR: Agent executable not found at: $agentExe" -ForegroundColor Red
    Write-Host "Please specify -AgentSourcePath or run build-msi.ps1 first." -ForegroundColor Yellow
    exit 1
}

# Stop existing service if running
Write-Host "Checking for existing installation..." -ForegroundColor Yellow
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Stopping existing service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# Create installation directory
Write-Host "Creating installation directory: $InstallPath" -ForegroundColor Yellow
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

# Copy agent files
Write-Host "Copying agent files..." -ForegroundColor Yellow
Copy-Item -Path "$AgentSourcePath\*" -Destination $InstallPath -Recurse -Force

# Parse tags
$tagsList = @()
if (-not [string]::IsNullOrEmpty($Tags)) {
    $tagsList = $Tags.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
}

# Create configuration
Write-Host "Creating configuration..." -ForegroundColor Yellow
$config = @{
    serverUrl = $ServerUrl
    registrationToken = $RegistrationToken
    pollIntervalSeconds = 30
    tags = $tagsList
    validateServerCertificate = (-not $SkipCertValidation)
}

if (-not [string]::IsNullOrEmpty($CertThumbprint)) {
    $config.certificateThumbprint = $CertThumbprint
    $config.validateServerCertificate = $true
}

$configPath = Join-Path $InstallPath "agent.json"
$config | ConvertTo-Json -Depth 10 | Set-Content -Path $configPath -Encoding UTF8
Write-Host "Configuration saved to: $configPath" -ForegroundColor Green

# Install/update service
$exePath = Join-Path $InstallPath "SysmonConfigPusher.Agent.exe"

if ($existingService) {
    Write-Host "Updating existing service..." -ForegroundColor Yellow
    # Service already exists, just update the binary path if needed
    sc.exe config $ServiceName binPath= "`"$exePath`"" | Out-Null
} else {
    Write-Host "Installing Windows service..." -ForegroundColor Yellow
    sc.exe create $ServiceName binPath= "`"$exePath`"" start= auto DisplayName= "Sysmon Config Pusher Agent" | Out-Null
    sc.exe description $ServiceName "Lightweight agent for SysmonConfigPusher - manages Sysmon on cloud-hosted Windows machines" | Out-Null
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
}

# Start service
Write-Host "Starting service..." -ForegroundColor Yellow
Start-Service -Name $ServiceName

# Wait for service to start
Start-Sleep -Seconds 3
$service = Get-Service -Name $ServiceName
if ($service.Status -eq "Running") {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " Installation Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Service Status: Running" -ForegroundColor Green
    Write-Host "Install Path:   $InstallPath" -ForegroundColor Cyan
    Write-Host "Server URL:     $ServerUrl" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "The agent will now register with the server and appear" -ForegroundColor White
    Write-Host "in the Computers list with 'Agent' as the source." -ForegroundColor White
    Write-Host ""
    Write-Host "To check logs: Get-WinEvent -LogName Application -MaxEvents 50 | Where-Object { `$_.ProviderName -eq 'SysmonConfigPusherAgent' }" -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "WARNING: Service installed but not running!" -ForegroundColor Yellow
    Write-Host "Status: $($service.Status)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Check Windows Event Log for errors." -ForegroundColor Yellow
}
