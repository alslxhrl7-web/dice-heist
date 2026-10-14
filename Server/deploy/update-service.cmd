@echo off
rem Double-click to rebuild and restart the DiceHeist service (asks for admin rights itself).
rem The DB with accounts and money (C:\DiceHeist\data) is kept.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-service.ps1"
echo.
pause
