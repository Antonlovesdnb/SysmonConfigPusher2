@echo off
echo Starting SysmonConfigPusher2 Development Environment
echo =====================================================

REM Start the backend in a new window
echo Starting .NET Backend (https://localhost:5001)...
start "SysmonConfigPusher Backend" cmd /k "cd /d C:\SysmonConfigPusher2\src\SysmonConfigPusher.Service && dotnet run"

REM Wait a moment for backend to start
timeout /t 3 /nobreak > nul

REM Start the frontend dev server in a new window
echo Starting React Frontend (http://localhost:5173)...
start "SysmonConfigPusher Frontend" cmd /k "cd /d C:\SysmonConfigPusher2\src\SysmonConfigPusher.Web && npm run dev"

echo.
echo Both servers starting in separate windows.
echo Backend:  https://localhost:5001
echo Frontend: http://localhost:5173
echo.
echo Press any key to exit this window (servers will keep running)...
pause > nul
