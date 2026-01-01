param(
    [string]$ServerUrl,
    [string]$RegistrationToken,
    [string]$SkipCertValidation = "1",
    [string]$PollInterval = "30",
    [string]$Tags = "",
    [string]$OutputPath
)

$validateCert = $SkipCertValidation -ne "1"
$pollInt = [int]$PollInterval
if ($pollInt -lt 10) { $pollInt = 30 }

$tagsList = @()
if ($Tags -and $Tags.Trim() -ne "") {
    $tagsList = $Tags.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
}

$config = @{
    serverUrl = $ServerUrl
    registrationToken = $RegistrationToken
    pollIntervalSeconds = $pollInt
    tags = $tagsList
    validateServerCertificate = $validateCert
}

$config | ConvertTo-Json -Depth 10 | Set-Content -Path $OutputPath -Encoding UTF8
