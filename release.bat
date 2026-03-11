@echo off
title AmpUp Release Script
cd /d "%~dp0"

if "%~1"=="" (
    echo Usage: release.bat ^<version^>
    echo Example: release.bat 0.4.0-alpha
    exit /b 1
)

set "VERSION=%~1"
:: Extract numeric part by splitting on "-"
for /f "tokens=1 delims=-" %%a in ("%VERSION%") do set "NUMERIC_VERSION=%%a"
set "ASSEMBLY_VERSION=%NUMERIC_VERSION%.0"

echo.
echo ========================================
echo   AmpUp Release v%VERSION%
echo ========================================
echo   Assembly version: %ASSEMBLY_VERSION%
echo.

:: -------------------------------------------------------
echo [1/7] Updating AmpUp.csproj versions...
powershell -NoProfile -Command "$f='AmpUp.csproj'; $x=[xml](Get-Content $f); $pg=$x.Project.PropertyGroup; $pg.Version='%VERSION%'; $pg.AssemblyVersion='%ASSEMBLY_VERSION%'; $pg.FileVersion='%ASSEMBLY_VERSION%'; $x.Save((Resolve-Path $f).Path)"
if errorlevel 1 (
    echo ERROR: Failed to update AmpUp.csproj
    exit /b 1
)
echo Done.

:: -------------------------------------------------------
echo [2/7] Generating installer\version.iss...
echo #define MyAppVersion "%VERSION%"> installer\version.iss
if errorlevel 1 (
    echo ERROR: Failed to generate version.iss
    exit /b 1
)
echo Done.

:: -------------------------------------------------------
echo [3/7] Staging AmpUp.csproj...
git add AmpUp.csproj
if errorlevel 1 (
    echo ERROR: Failed to stage files
    exit /b 1
)
echo Done.

:: -------------------------------------------------------
echo [4/7] Committing...
git -c user.name="Tyson Wolf" -c user.email="audioslayer@gmail.com" commit -m "release: v%VERSION%"
if errorlevel 1 (
    echo ERROR: Failed to commit
    exit /b 1
)
echo Done.

:: -------------------------------------------------------
echo [5/7] Tagging v%VERSION%...
git -c user.name="Tyson Wolf" -c user.email="audioslayer@gmail.com" tag "v%VERSION%"
if errorlevel 1 (
    echo ERROR: Failed to create tag
    exit /b 1
)
echo Done.

:: -------------------------------------------------------
echo [6/7] Pushing to origin master with tags...
git push origin master --tags
if errorlevel 1 (
    echo ERROR: Failed to push
    exit /b 1
)
echo Done.

:: -------------------------------------------------------
echo [7/7] Release initiated!
echo.
echo ========================================
echo   v%VERSION% pushed successfully!
echo   GitHub Actions will now build the
echo   installer and create the release.
echo ========================================
echo.
