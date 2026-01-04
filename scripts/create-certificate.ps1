<#
.SYNOPSIS
    Creates a self-signed certificate for SysmonConfigPusher HTTPS.

.DESCRIPTION
    Generates a self-signed certificate and stores it in the LocalMachine
    certificate store. The certificate is valid for 2 years.

.PARAMETER DnsName
    The DNS name for the certificate. Defaults to the local computer name.

.EXAMPLE
    .\create-certificate.ps1

.EXAMPLE
    .\create-certificate.ps1 -DnsName "sysmonpusher.contoso.com"
#>

param(
    [string]$DnsName = $env:COMPUTERNAME
)

$ErrorActionPreference = "Stop"

Write-Host "Creating self-signed certificate for SysmonConfigPusher..." -ForegroundColor Cyan
Write-Host "  DNS Name: $DnsName"

# Check if certificate already exists
$existingCert = Get-ChildItem Cert:\LocalMachine\My | Where-Object {
    $_.Subject -eq "CN=SysmonConfigPusher" -and $_.NotAfter -gt (Get-Date)
}

if ($existingCert) {
    Write-Host "  Existing valid certificate found: $($existingCert.Thumbprint)" -ForegroundColor Yellow
    Write-Host "  Skipping certificate creation."
    exit 0
}

# Create self-signed certificate
$cert = New-SelfSignedCertificate `
    -Subject "CN=SysmonConfigPusher" `
    -DnsName $DnsName, "localhost", $env:COMPUTERNAME `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears(2) `
    -FriendlyName "SysmonConfigPusher HTTPS Certificate"

Write-Host ""
Write-Host "Certificate created successfully!" -ForegroundColor Green
Write-Host "  Subject: $($cert.Subject)"
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  Expires: $($cert.NotAfter)"
Write-Host ""
Write-Host "The certificate is configured in appsettings.json" -ForegroundColor Cyan
