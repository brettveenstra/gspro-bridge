@echo off
REM GSProBridge Build Script (Wrapper)
REM Clone -> Run -> Done

setlocal

REM Check if PowerShell is available (it should be on all modern Windows)
where pwsh >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    REM PowerShell 7+ found (preferred)
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
) else (
    where powershell >nul 2>&1
    if %ERRORLEVEL% EQU 0 (
        REM Windows PowerShell 5.1 found
        powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
    ) else (
        echo Error: PowerShell not found
        echo Please install PowerShell from https://aka.ms/powershell
        exit /b 1
    )
)

exit /b %ERRORLEVEL%
