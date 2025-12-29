Write-Host "Profile Path: $PROFILE"
Write-Host "Profile Exists: $(Test-Path $PROFILE)"
if (Test-Path $PROFILE) {
    Write-Host "`nProfile Content:"
    Get-Content $PROFILE
}
