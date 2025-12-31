#Requires -RunAsAdministrator
#Requires -Modules ActiveDirectory

<#
.SYNOPSIS
    Sets up a Group Managed Service Account (gMSA) for SysmonConfigPusher.

.DESCRIPTION
    This script creates a gMSA in Active Directory and configures the
    SysmonConfigPusher service to use it. The gMSA provides automatic
    password management and secure network authentication.

.PARAMETER AccountName
    Name of the gMSA to create (default: svc-SysmonPusher)

.PARAMETER SkipAccountCreation
    Skip creating the gMSA (use if it already exists)

.EXAMPLE
    .\setup-gmsa.ps1

.EXAMPLE
    .\setup-gmsa.ps1 -AccountName "svc-MySysmonPusher"

.EXAMPLE
    .\setup-gmsa.ps1 -SkipAccountCreation
#>

param(
    [string]$AccountName = "svc-SysPusher",  # Max 15 chars for samAccountName
    [switch]$SkipAccountCreation
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n>> $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "   [OK] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "   [WARN] $Message" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host "   [FAIL] $Message" -ForegroundColor Red
}

# Banner
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SysmonConfigPusher gMSA Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Get domain info
$domain = Get-ADDomain
$domainDns = $domain.DNSRoot
$domainNetbios = $domain.NetBIOSName
$serverName = $env:COMPUTERNAME

Write-Host "`nDomain: $domainNetbios ($domainDns)"
Write-Host "Server: $serverName"
Write-Host "gMSA Name: $AccountName"

if (-not $SkipAccountCreation) {
    # Step 1: Check/Create KDS Root Key
    Write-Step "Checking KDS Root Key..."

    $kdsKey = Get-KdsRootKey -ErrorAction SilentlyContinue
    if (-not $kdsKey) {
        Write-Warning "No KDS Root Key found. Creating one..."
        Write-Host ""
        Write-Host "   Options:" -ForegroundColor Yellow
        Write-Host "   1) Lab/Test: Create backdated key (works immediately)"
        Write-Host "   2) Production: Create key and wait 10 hours for replication"
        Write-Host ""
        $choice = Read-Host "Choose option (1 or 2)"

        if ($choice -eq '1') {
            # Backdate by 10 hours so it's immediately effective
            Add-KdsRootKey -EffectiveTime ((Get-Date).AddHours(-10)) | Out-Null
            Write-Success "KDS Root Key created (backdated for immediate use)"
        } elseif ($choice -eq '2') {
            Add-KdsRootKey -EffectiveImmediately | Out-Null
            Write-Warning "KDS Root Key created - you must wait 10 hours before continuing"
            Write-Host "   Run this script again after replication completes."
            exit 0
        } else {
            Write-Fail "Invalid choice"
            exit 1
        }
    } else {
        Write-Success "KDS Root Key exists (EffectiveTime: $($kdsKey.EffectiveTime))"
    }

    # Step 2: Create gMSA
    Write-Step "Creating gMSA account..."

    $existingAccount = Get-ADServiceAccount -Filter "Name -eq '$AccountName'" -ErrorAction SilentlyContinue
    if ($existingAccount) {
        Write-Success "gMSA '$AccountName' already exists"
    } else {
        try {
            New-ADServiceAccount -Name $AccountName `
                -DNSHostName "$AccountName.$domainDns" `
                -PrincipalsAllowedToRetrieveManagedPassword "$serverName$" `
                -Description "Service account for SysmonConfigPusher" `
                -Enabled $true

            Write-Success "gMSA '$AccountName' created"
        } catch {
            Write-Fail "Failed to create gMSA: $_"
            exit 1
        }
    }

    # Step 2b: Ensure this server can retrieve the password
    Write-Step "Granting password retrieval permission to $serverName..."

    try {
        $gMSA = Get-ADServiceAccount -Identity $AccountName -Properties PrincipalsAllowedToRetrieveManagedPassword
        $serverSid = (Get-ADComputer $serverName).SID.Value

        $currentPrincipals = $gMSA.PrincipalsAllowedToRetrieveManagedPassword
        if ($currentPrincipals -notcontains $serverSid) {
            Set-ADServiceAccount -Identity $AccountName -PrincipalsAllowedToRetrieveManagedPassword @($serverSid)
            Write-Success "Permission granted"
        } else {
            Write-Success "Permission already granted"
        }
    } catch {
        Write-Warning "Could not update permissions: $_"
        Write-Warning "You may need to manually grant '$serverName$' permission to retrieve the gMSA password"
    }
}

# Step 3: Install gMSA on this server
Write-Step "Installing gMSA on local server..."

try {
    Install-ADServiceAccount -Identity $AccountName -ErrorAction Stop
    Write-Success "gMSA installed on $serverName"
} catch {
    if ($_.Exception.Message -like "*already installed*") {
        Write-Success "gMSA already installed on $serverName"
    } else {
        Write-Fail "Failed to install gMSA: $_"
        exit 1
    }
}

# Step 4: Test gMSA
Write-Step "Testing gMSA..."

$testResult = Test-ADServiceAccount -Identity $AccountName
if ($testResult) {
    Write-Success "gMSA test passed"
} else {
    Write-Fail "gMSA test failed - the account may not be properly configured"
    exit 1
}

# Step 5: Configure service
Write-Step "Configuring SysmonConfigPusher service..."

$serviceName = "SysmonConfigPusher"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if (-not $service) {
    Write-Fail "Service '$serviceName' not found. Is SysmonConfigPusher installed?"
    exit 1
}

# Stop service if running
if ($service.Status -eq 'Running') {
    Write-Host "   Stopping service..."
    Stop-Service $serviceName -Force
    Start-Sleep -Seconds 2
}

# Configure service to use gMSA
$gMSAAccount = "$domainNetbios\$AccountName$"
$result = & sc.exe config $serviceName obj= $gMSAAccount password= ""

if ($LASTEXITCODE -eq 0) {
    Write-Success "Service configured to use $gMSAAccount"
} else {
    Write-Fail "Failed to configure service: $result"
    exit 1
}

# Start service
Write-Host "   Starting service..."
Start-Service $serviceName
Start-Sleep -Seconds 2

$service = Get-Service -Name $serviceName
if ($service.Status -eq 'Running') {
    Write-Success "Service started successfully"
} else {
    Write-Fail "Service failed to start. Check Event Viewer for details."
    exit 1
}

# Summary
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "gMSA Account: $gMSAAccount"
Write-Host "Service Status: Running"
Write-Host ""
Write-Host "IMPORTANT: Next Steps" -ForegroundColor Yellow
Write-Host "---------------------"
Write-Host "The gMSA needs local admin rights on target endpoints for WMI access."
Write-Host ""
Write-Host "Option A: Add to a GPO (Recommended for many endpoints)"
Write-Host "  1. Open Group Policy Management"
Write-Host "  2. Edit a GPO linked to target OUs"
Write-Host "  3. Navigate to: Computer Config > Policies > Windows Settings >"
Write-Host "     Security Settings > Restricted Groups"
Write-Host "  4. Add '$gMSAAccount' to 'Administrators'"
Write-Host ""
Write-Host "Option B: Add manually (For few endpoints)"
Write-Host "  Run on each target:"
Write-Host "  Add-LocalGroupMember -Group 'Administrators' -Member '$gMSAAccount'"
Write-Host ""
