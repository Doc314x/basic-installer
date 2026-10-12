@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM  Build-Skript fuer Basic Software Installer
REM  Schritt 1: .NET-Release-Build der WinForms-App
REM  Schritt 2: optional Inno Setup -> Setup.exe (falls installiert)
REM ============================================================

set "ROOT=%~dp0"
set "PROJECT=%ROOT%src\BasicInstaller\BasicInstaller.csproj"
set "EXE_PATH=%ROOT%src\BasicInstaller\bin\Release\net48\BasicInstaller.exe"
set "ISS_SCRIPT=%ROOT%installer\setup.iss"
set "SETUP_EXE=%ROOT%installer\Output\BasicInstaller-Setup.exe"

echo ============================================================
echo  Schritt 1/2: .NET-Release-Build
echo ============================================================
dotnet build "%PROJECT%" -c Release
if errorlevel 1 (
    echo.
    echo [FEHLER] dotnet build ist fehlgeschlagen.
    exit /b 1
)

echo.
echo ============================================================
echo  Schritt 2/2: Installer mit Inno Setup erzeugen (optional)
echo ============================================================

REM Inno-Setup-Compiler suchen: zuerst im PATH, sonst Standardpfad.
set "ISCC="
where iscc.exe >nul 2>nul
if not errorlevel 1 (
    set "ISCC=iscc.exe"
) else (
    if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
        set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    )
)

if "%ISCC%"=="" (
    echo [HINWEIS] Inno Setup Compiler ^(iscc.exe^) wurde nicht gefunden.
    echo           Installer-Schritt wird uebersprungen - fuer die reine
    echo           Entwicklung nicht erforderlich. Zum Erzeugen eines
    echo           Setups Inno Setup 6 installieren: https://jrsoftware.org/isinfo.php
) else (
    echo Verwende Inno Setup Compiler: %ISCC%
    "%ISCC%" "%ISS_SCRIPT%"
    if errorlevel 1 (
        echo.
        echo [FEHLER] Inno-Setup-Build ist fehlgeschlagen.
        exit /b 1
    )
)

echo.
echo ============================================================
echo  Fertig
echo ============================================================
echo Programm (exe):   %EXE_PATH%
if exist "%SETUP_EXE%" (
    echo Setup ^(Installer^): %SETUP_EXE%
) else (
    echo Setup ^(Installer^): nicht erzeugt ^(Inno Setup nicht gefunden oder uebersprungen^)
)

endlocal
