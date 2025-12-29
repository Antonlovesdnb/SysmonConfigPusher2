$PROFILE | Out-File -FilePath C:\SysmonConfigPusher2\profile_path.txt -Encoding UTF8
(Test-Path $PROFILE) | Out-File -FilePath C:\SysmonConfigPusher2\profile_exists.txt -Encoding UTF8
if (Test-Path $PROFILE) {
    Get-Content $PROFILE | Out-File -FilePath C:\SysmonConfigPusher2\profile_content.txt -Encoding UTF8
}
