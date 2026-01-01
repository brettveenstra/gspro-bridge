@echo off
REM GSProBridge Build Script (Wrapper)
REM Clone -> Run -> Done

setlocal

REM Check if PowerShell is available (it should be on all modern Windows)
where pwsh >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    REM PowerShell 7+ found in PATH (preferred)
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
) else (
    REM Check default PowerShell 7 MSI install location
    if exist "C:\Program Files\PowerShell\7\pwsh.exe" (
        "C:\Program Files\PowerShell\7\pwsh.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
    ) else (
        where powershell >nul 2>&1
        if %ERRORLEVEL% EQU 0 (
            REM Windows PowerShell 5.1 found (fallback - may have issues with UTF-8 box chars)
            powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
        ) else (
            echo Error: PowerShell not found
            echo Please install PowerShell from https://aka.ms/powershell
            exit /b 1
        )
    )
)

exit /b %ERRORLEVEL%
