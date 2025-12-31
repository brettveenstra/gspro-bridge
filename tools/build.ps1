#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build script for GSProBridge

.DESCRIPTION
    Builds the GSProBridge solution with configurable options.
    Clone → Run → Done.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release

.PARAMETER Clean
    Clean before building

.PARAMETER Test
    Run tests after build

.PARAMETER Publish
    Publish self-contained executable after build

.EXAMPLE
    .\build.ps1
    Build Release configuration

.EXAMPLE
    .\build.ps1 -Configuration Debug -Test
    Build Debug and run tests

.EXAMPLE
    .\build.ps1 -Clean -Publish
    Clean, build Release, and publish executable
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Clean,
    [switch]$Test,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Script paths
$ScriptRoot = $PSScriptRoot
$RepoRoot = Split-Path $ScriptRoot -Parent
$SolutionFile = Join-Path $RepoRoot "GSProBridge.sln"

# Colors for output
function Write-Step {
    param([string]$Message)
    Write-Host "`n===> $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Failure {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

# Main build script
try {
    Write-Host @"

╔═══════════════════════════════════════════════════════════╗
║            GSProBridge Build Script                       ║
║  Clone → Run → Done                                       ║
╚═══════════════════════════════════════════════════════════╝

"@ -ForegroundColor Yellow

    # Check prerequisites
    Write-Step "Checking prerequisites"

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Failure ".NET SDK not found"
        Write-Host "Please install .NET SDK 8.0 or later: https://dot.net/download" -ForegroundColor Yellow
        exit 1
    }

    $dotnetVersion = dotnet --version
    Write-Success ".NET SDK $dotnetVersion found"

    if (-not (Test-Path $SolutionFile)) {
        Write-Failure "Solution file not found: $SolutionFile"
        exit 1
    }

    Write-Success "Solution file found"

    # Clean
    if ($Clean) {
        Write-Step "Cleaning solution"
        dotnet clean $SolutionFile -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            Write-Failure "Clean failed"
            exit $LASTEXITCODE
        }
        Write-Success "Clean completed"
    }

    # Restore
    Write-Step "Restoring NuGet packages"
    dotnet restore $SolutionFile
    if ($LASTEXITCODE -ne 0) {
        Write-Failure "Restore failed"
        exit $LASTEXITCODE
    }
    Write-Success "Restore completed"

    # Build
    Write-Step "Building solution ($Configuration)"
    dotnet build $SolutionFile -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Failure "Build failed"
        exit $LASTEXITCODE
    }
    Write-Success "Build completed"

    # Test
    if ($Test) {
        Write-Step "Running tests"
        dotnet test $SolutionFile -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) {
            Write-Failure "Tests failed"
            exit $LASTEXITCODE
        }
        Write-Success "Tests passed"
    }

    # Publish
    if ($Publish) {
        Write-Step "Publishing self-contained executable"
        $ProjectFile = Join-Path $RepoRoot "src/GSProBridge/GSProBridge.csproj"
        $PublishDir = Join-Path $RepoRoot "publish"

        dotnet publish $ProjectFile `
            -c $Configuration `
            -r win-x64 `
            --self-contained `
            -p:PublishSingleFile=true `
            -p:DebugType=embedded `
            -o $PublishDir

        if ($LASTEXITCODE -ne 0) {
            Write-Failure "Publish failed"
            exit $LASTEXITCODE
        }

        Write-Success "Published to: $PublishDir"
        Write-Host "`nExecutable: $PublishDir\GSProBridge.exe" -ForegroundColor Cyan
    }

    # Success summary
    Write-Host @"

╔═══════════════════════════════════════════════════════════╗
║            ✓ Build Successful                             ║
╚═══════════════════════════════════════════════════════════╝

Configuration: $Configuration

"@ -ForegroundColor Green

    if ($Publish) {
        Write-Host "Next step: Run publish\GSProBridge.exe" -ForegroundColor Yellow
    } else {
        Write-Host "Next step: .\tools\build.ps1 -Publish" -ForegroundColor Yellow
    }

    exit 0
}
catch {
    Write-Host "`n" -NoNewline
    Write-Failure "Build failed with error:"
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    exit 1
}
