@echo off
setlocal
echo ============================================
echo   Amp Up - Build Installer
echo ============================================
echo.

:: Extract version from .csproj before publishing so the exe, installer
:: metadata, and output filename all use the same source of truth.
set APP_VERSION=
set APP_ASSEMBLY_VERSION=
set APP_FILE_VERSION=
for /f "tokens=3 delims=<>" %%v in ('findstr /c:"<Version>" "%~dp0AmpUp.csproj"') do set APP_VERSION=%%v
for /f "tokens=3 delims=<>" %%v in ('findstr /c:"<AssemblyVersion>" "%~dp0AmpUp.csproj"') do set APP_ASSEMBLY_VERSION=%%v
for /f "tokens=3 delims=<>" %%v in ('findstr /c:"<FileVersion>" "%~dp0AmpUp.csproj"') do set APP_FILE_VERSION=%%v
if "%APP_VERSION%"=="" (
    echo ERROR: Could not extract version from AmpUp.csproj!
    echo        Make sure AmpUp.csproj has a ^<Version^> tag.
    pause
    exit /b 1
)
set APP_VERSION_NUM=%APP_VERSION%
for /f "tokens=1 delims=-+" %%v in ("%APP_VERSION%") do set APP_VERSION_NUM=%%v
for /f "tokens=1-4 delims=." %%a in ("%APP_VERSION_NUM%") do (
    set APP_MAJOR=%%a
    set APP_MINOR=%%b
    set APP_PATCH=%%c
    set APP_REVISION=%%d
)
if "%APP_MINOR%"=="" set APP_MINOR=0
if "%APP_PATCH%"=="" set APP_PATCH=0
if "%APP_REVISION%"=="" set APP_REVISION=0
if "%APP_ASSEMBLY_VERSION%"=="" set APP_ASSEMBLY_VERSION=%APP_MAJOR%.%APP_MINOR%.%APP_PATCH%.%APP_REVISION%
if "%APP_FILE_VERSION%"=="" set APP_FILE_VERSION=%APP_ASSEMBLY_VERSION%
echo      Version: %APP_VERSION%
echo      Assembly: %APP_ASSEMBLY_VERSION%
echo.

:: Clean previous publish output
if exist publish rmdir /s /q publish
if exist installer\output rmdir /s /q installer\output

:: Publish self-contained (includes .NET runtime — no install required for user)
echo [1/2] Publishing Amp Up (self-contained)...
dotnet publish "%~dp0AmpUp.csproj" -c Release -r win-x64 --self-contained -o "%~dp0publish" -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:Version=%APP_VERSION% -p:AssemblyVersion=%APP_ASSEMBLY_VERSION% -p:FileVersion=%APP_FILE_VERSION% -p:InformationalVersion=%APP_VERSION%
if errorlevel 1 (
    echo ERROR: dotnet publish failed!
    pause
    exit /b 1
)
echo      Published to .\publish\
echo.

echo #define MyAppVersion "%APP_VERSION%" > "%~dp0installer\version.iss"
echo #define MyAppFileVersion "%APP_FILE_VERSION%" >> "%~dp0installer\version.iss"
echo.

:: Build installer with Inno Setup
echo [2/2] Building installer...
set "ISCC="
for /f "delims=" %%I in ('where iscc 2^>nul') do if not defined ISCC set "ISCC=%%I"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
    echo ERROR: Inno Setup not found! Install from https://jrsoftware.org/isinfo.php
    echo        Then re-run this script.
    pause
    exit /b 1
)
"%ISCC%" installer\ampup-setup.iss

if errorlevel 1 (
    echo ERROR: Installer build failed!
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Done! Installer at:
echo   installer\output\AmpUp-Setup-%APP_VERSION%.exe
echo ============================================
pause
