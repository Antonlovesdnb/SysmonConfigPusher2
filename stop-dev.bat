@echo off
echo Stopping SysmonConfigPusher2 Development Servers...

REM Kill dotnet processes running from this project
taskkill /FI "WINDOWTITLE eq SysmonConfigPusher Backend*" /F 2>nul
taskkill /FI "WINDOWTITLE eq SysmonConfigPusher Frontend*" /F 2>nul

REM Also kill any orphaned processes
taskkill /IM "dotnet.exe" /F 2>nul
taskkill /FI "WINDOWTITLE eq npm*" /F 2>nul

echo Done.
