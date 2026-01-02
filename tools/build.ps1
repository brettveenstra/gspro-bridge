#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build script for GSProBridge

.DESCRIPTION
    Builds the GSProBridge solution with configurable options.
    Clone → Run → Done.

    CI/CD FIRST: Tests run by DEFAULT. Build without tests is NOT a successful build.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release

.PARAMETER Clean
    Clean before building

.PARAMETER BuildOnly
    Build only, skip tests (use for fast iteration during development)

.PARAMETER Publish
    Publish self-contained executable after build

.EXAMPLE
    .\build.ps1
    Build + Test (CI/CD standard, RECOMMENDED)

.EXAMPLE
    .\build.ps1 -Configuration Debug
    Build + Test in Debug mode

.EXAMPLE
    .\build.ps1 -Clean -Publish
    Clean, build, test, and publish executable

.EXAMPLE
    .\build.ps1 -BuildOnly
    Build only without tests (fast iteration, NOT validated)
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Clean,
    [switch]$BuildOnly,
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
║               GSProBridge Build Script                    ║
║                  Clone → Run → Done                       ║
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

    # Tests (run on test project directly for cleaner output)
    $testSummary = ""
    if (-not $BuildOnly) {
        Write-Step "Running tests"

        # Find test project (assuming single test project for now)
        $TestProject = Join-Path $RepoRoot "tests/GSProBridge.Tests/GSProBridge.Tests.csproj"

        # Run tests - if failures occur, dotnet test writes full output to stdout
        # Capture everything and show failures when needed
        $testOutput = dotnet test $TestProject -c $Configuration --no-build --verbosity normal 2>&1 | Out-String
        $testExitCode = $LASTEXITCODE

        # Show key test info (discovery count and platform issues only)
        $testOutput -split "`n" | Where-Object {
            $_ -match 'NUnit3TestExecutor discovered' -or
            $_ -match 'Only supported on'
        } | ForEach-Object { Write-Host $_ }

        if ($testExitCode -ne 0) {
            Write-Failure "Tests failed"

            # Just show the full test output - all error details are already there
            Write-Host "`n" # Blank line for readability
            Write-Host $testOutput -ForegroundColor DarkYellow

            Write-Host "`nBuild FAILED: Tests must pass for validated build" -ForegroundColor Red
            exit $testExitCode
        }

        # Parse test summary from VSTest/NUnit output
        # Actual format:
        #   Total tests: 40
        #        Passed: 40
        #   Total time: 0.7314 Seconds
        $totalTests = 0
        $passedTests = 0
        $failedTests = 0
        $testTime = ""

        # Check for platform compatibility issues
        if ($testOutput -match "Only supported on") {
            $testSummary = "Skipped (requires Windows - run from Windows PowerShell) ⚠️"
        }
        # Parse VSTest format
        elseif ($testOutput -match 'Total tests:\s*(\d+)') {
            $totalTests = [int]$matches[1]

            # Extract passed count
            if ($testOutput -match 'Passed:\s*(\d+)') {
                $passedTests = [int]$matches[1]
            }

            # Extract failed count (if present)
            if ($testOutput -match 'Failed:\s*(\d+)') {
                $failedTests = [int]$matches[1]
            }

            # Extract duration
            if ($testOutput -match 'Total time:\s*([\d.]+)\s*Seconds?') {
                $testTime = "$([math]::Round([double]$matches[1], 1))s"
            }

            if ($totalTests -gt 0) {
                if ($failedTests -gt 0) {
                    $testSummary = "$passedTests/$totalTests passed, $failedTests FAILED in $testTime ✗"
                }
                else {
                    $testSummary = "$totalTests/$totalTests passed in $testTime ✓"
                }
            }
            else {
                $testSummary = "No tests executed (check output above)"
            }
        }
        else {
            # Fallback if parsing failed
            $testSummary = "Tests completed (check output above)"
        }

        Write-Success $testSummary
    }
    else {
        Write-Host "`n⚠️  WARNING: Build-only mode (-BuildOnly)" -ForegroundColor Yellow
        Write-Host "   Tests NOT run. This is NOT a validated build." -ForegroundColor Yellow
        Write-Host "   Use for fast iteration only, NOT for commits/CI/CD." -ForegroundColor Yellow
        $testSummary = "NOT RUN (build-only mode) ⚠️"
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
