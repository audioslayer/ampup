@echo off
echo ============================================
echo   Amp Up - Build Installer
echo ============================================
echo.

:: Clean previous publish output
if exist publish rmdir /s /q publish
if exist installer\output rmdir /s /q installer\output

:: Publish self-contained single-directory output
echo [1/2] Publishing Amp Up...
dotnet publish -c Release -r win-x64 --self-contained -o publish -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
    echo ERROR: dotnet publish failed!
    pause
    exit /b 1
)
echo      Published to .\publish\
echo.

:: Build installer with Inno Setup
echo [2/2] Building installer...
where iscc >nul 2>nul
if errorlevel 1 (
    :: Try default Inno Setup install path
    if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ampup-setup.iss
    ) else (
        echo ERROR: Inno Setup not found! Install from https://jrsoftware.org/isinfo.php
        echo        Then re-run this script.
        pause
        exit /b 1
    )
) else (
    iscc installer\ampup-setup.iss
)

if errorlevel 1 (
    echo ERROR: Installer build failed!
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Done! Installer at:
echo   installer\output\AmpUp-Setup-0.3-alpha.exe
echo ============================================
pause
