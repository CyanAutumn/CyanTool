@echo off
setlocal EnableExtensions


chcp 65001 >nul

set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%PublishUpdateRelease.ps1" %*

echo.
pause
