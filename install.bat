@echo off
:: ============================================
:: KeywordGuard Pro - Installer (NEU!)
:: Startet FINAL-Install.ps1 als Administrator
:: ============================================

:: Pruefe ob als Admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo.
    echo Starte als Administrator...
    echo.
    powershell -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

:: Wechsle in den Ordner der BAT
cd /d "%~dp0"
echo.
echo ========================================
echo    KEYWORD GUARD PRO - INSTALLATION
echo ========================================
echo.

:: Starte das Installations-Skript
powershell -NoProfile -ExecutionPolicy Bypass -File "FINAL-Install.ps1"

echo.
echo Installation abgeschlossen.
pause