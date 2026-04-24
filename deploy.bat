@echo off
title Amp Up Deploy
echo ==========================================
echo   Amp Up - Pull and Build
echo ==========================================
echo.

cd /d "%~dp0"

echo [1/5] Pulling latest from GitHub...
git fetch origin
git reset --hard origin/master
if errorlevel 1 (
    echo ERROR: git pull failed. Check your connection or conflicts.
    pause
    exit /b 1
)

echo.
echo [2/5] Killing old Amp Up if running...
taskkill /f /im AmpUp.exe 2>nul
timeout /t 3 /nobreak >nul

echo.
echo [3/5] Cleaning stale WPF temp build files...
for %%D in ("obj\Debug\net8.0-windows" "obj\Release\net8.0-windows") do (
    if exist %%~D (
        del /q "%%~D\AmpUp_*_wpftmp.*" 2>nul
    )
)

echo.
echo [4/5] Building...
dotnet build AmpUp.sln -c Debug --no-incremental
if errorlevel 1 (
    echo ERROR: Build failed. Check errors above.
    pause
    exit /b 1
)

echo.
echo [5/5] Launching Amp Up...
start "" "%~dp0bin\Debug\net8.0-windows\AmpUp.exe"

echo.
echo Amp Up deployed and launched.
timeout /t 2 /nobreak >nul
