@echo off
setlocal
title TSWeb Node.js One-Click Installer

rem ============================================================
rem  TSWeb - Node.js One-Click Installer
rem  ============================================================
rem  Usage:
rem    install-node.bat            auto: check -> download -> silent install -> verify
rem    install-node.bat force      reinstall even if Node.js already exists
rem    install-node.bat check      only check status, do nothing else
rem
rem  Behavior (designed to have NO branch options for end users):
rem    1. If Node.js is already installed -> report and exit (use "force" to reinstall)
rem    2. If no node MSI next to this script -> auto download latest LTS from nodejs.org
rem    3. msiexec /qn full silent install (default features, no dialogs at all)
rem    4. Verify node.exe and npm
rem  ============================================================

set "SCRIPT_DIR=%~dp0"
set "MSI_FILE=%SCRIPT_DIR%node-lts-x64.msi"
set "MODE="
if /i "%~1"=="force" set "MODE=force"
if /i "%~1"=="check" set "MODE=check"
if /i "%~1"=="nopause" set "MODE=nopause"

rem ------------------------------------------------------------
rem check mode: report only
rem ------------------------------------------------------------
if "%MODE%"=="check" (
    echo [Check] Looking for Node.js on PATH...
    where node >nul 2>nul
    if %errorlevel%==0 (
        echo [Check] Node.js found:
        node --version
        echo [Check] npm:
        call npm --version 2>nul
        echo [Check] OK - Node.js is already available.
    ) else (
        echo [Check] Node.js NOT found on PATH.
        if exist "%MSI_FILE%" (
            echo [Check] Local MSI found: %MSI_FILE%
        ) else (
            echo [Check] No local MSI, will download latest LTS on install.
        )
    )
    exit /b 0
)

rem ------------------------------------------------------------
rem if already installed and not forced -> skip (no branches for users)
rem ------------------------------------------------------------
if not "%MODE%"=="force" (
    where node >nul 2>nul
    if %errorlevel%==0 (
        echo [Skip] Node.js already installed:
        node --version
        echo [Skip] Nothing to do. Use "install-node.bat force" to reinstall.
        echo.
        pause
        exit /b 0
    )
)

rem ------------------------------------------------------------
rem self-elevate to administrator (MSI system-wide install needs admin)
rem ------------------------------------------------------------
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [Elevate] Requesting administrator privileges...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
    exit /b 0
)

echo ============================================================
echo  TSWeb - Node.js Installer
echo ============================================================

rem ------------------------------------------------------------
rem ensure MSI exists, otherwise download latest LTS
rem ------------------------------------------------------------
if not exist "%MSI_FILE%" (
    echo [Download] Downloading latest Node.js LTS MSI (x64) from nodejs.org...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $j=Invoke-RestMethod 'https://nodejs.org/dist/index.json'; $l=$j|Where-Object{$_.lts -ne $false}|Select-Object -First 1; $v=$l.version.TrimStart('v'); $url='https://nodejs.org/dist/'+$l.version+'/node-'+$v+'-x64.msi'; Write-Host ('Downloading: '+$url); Invoke-WebRequest $url -OutFile '%MSI_FILE%'; Write-Host ('Saved: '+'%MSI_FILE%')"
    if %errorlevel% neq 0 (
        echo [Error] Download failed. Please place node.msi next to this script manually.
        echo         Expected file name: %MSI_FILE%
        echo.
        pause
        exit /b 1
    )
)

rem ------------------------------------------------------------
rem silent install: /qn = no UI, default features, no reboot
rem ------------------------------------------------------------
echo [Install] Running silent install, this may take a minute...
msiexec /i "%MSI_FILE%" /qn /norestart
set "INSTALL_RC=%errorlevel%"

if "%INSTALL_RC%"=="0" goto :verify
if "%INSTALL_RC%"=="3010" (
    echo [Install] Done, reboot recommended.
    goto :verify
)
if "%INSTALL_RC%"=="1618" (
    echo [Error] Another installation is in progress. Please retry later.
    echo.
    pause
    exit /b 1
)
echo [Error] msiexec failed with code %INSTALL_RC%.
echo.
pause
exit /b 1

:verify
echo [Verify] Checking installed Node.js...
if exist "%ProgramFiles%\nodejs\node.exe" (
    echo [Verify] Node.js version:
    "%ProgramFiles%\nodejs\node.exe" --version
    echo [Verify] npm version:
    call "%ProgramFiles%\nodejs\npm.cmd" --version 2>nul
    echo.
    echo [OK] Node.js installed successfully.
    echo [Note] PATH was updated by the installer; open a NEW terminal window to use node/npm.
) else (
    echo [Warn] Installed but node.exe not found at %ProgramFiles%\nodejs\node.exe
    echo        Please check manually.
)
echo.
pause
exit /b 0
