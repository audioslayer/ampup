@echo off
title WolfMixer Deploy
echo ==========================================
echo   WolfMixer - Pull and Build
echo ==========================================
echo.

cd /d "%~dp0"

echo [1/3] Pulling latest from GitHub...
git pull
if errorlevel 1 (
    echo ERROR: git pull failed. Check your connection or conflicts.
    pause
    exit /b 1
)

echo.
echo [2/3] Building...
dotnet build -c Debug
if errorlevel 1 (
    echo ERROR: Build failed. Check errors above.
    pause
    exit /b 1
)

echo.
echo [3/3] Done! Killing old WolfMixer if running...
taskkill /f /im WolfMixer.exe 2>nul
timeout /t 1 /nobreak >nul

echo Launching WolfMixer...
start "" "%~dp0bin\Debug\net8.0-windows\WolfMixer.exe"

echo.
echo WolfMixer deployed and launched.
pause
