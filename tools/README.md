# GSProBridge Build Tools

**Clone → Run → Done**

Simple build automation for GSProBridge. No complex setup required.

## Quick Start

### Windows (Command Prompt or PowerShell)

```cmd
git clone https://github.com/yourusername/gspro-bridge.git
cd gspro-bridge
tools\build.bat
```

### Windows (PowerShell preferred)

```powershell
git clone https://github.com/yourusername/gspro-bridge.git
cd gspro-bridge
.\tools\build.ps1
```

That's it! The build script will:
- ✓ Check for .NET SDK
- ✓ Restore NuGet packages
- ✓ Build Release configuration
- ✓ Report success/failure

## Build Options

### Basic Usage

```powershell
# Build Release (default)
.\tools\build.ps1

# Build Debug
.\tools\build.ps1 -Configuration Debug

# Clean before build
.\tools\build.ps1 -Clean

# Build and run tests
.\tools\build.ps1 -Test

# Build and publish self-contained executable
.\tools\build.ps1 -Publish
```

### Combined Options

```powershell
# Clean, build, test, publish
.\tools\build.ps1 -Clean -Test -Publish

# Debug build with tests
.\tools\build.ps1 -Configuration Debug -Test
```

## Build Script Features

### Prerequisites Check
- Verifies .NET SDK installed (8.0+)
- Clear error messages if prerequisites missing
- Solution file validation

### Build Modes
- **Debug**: Development build with debug symbols
- **Release**: Optimized build (default)

### Clean Build
- Removes previous build artifacts
- Ensures fresh compilation

### Test Execution
- Runs all unit tests after build
- Fails build if tests fail
- Skipped by default (opt-in with `-Test`)

### Publish Executable
- Creates self-contained Windows executable
- Single-file publish (no external dependencies)
- Output: `publish/GSProBridge.exe`
- Embedded debug symbols
- Ready for distribution

## Prerequisites

### .NET SDK 8.0+

**Windows:**
```powershell
winget install Microsoft.DotNet.SDK.8
```

Or download from: https://dot.net/download

**Verify installation:**
```powershell
dotnet --version
```

## Output Locations

| Build Type | Output Location |
|------------|----------------|
| Debug build | `src/GSProBridge/bin/Debug/net8.0/` |
| Release build | `src/GSProBridge/bin/Release/net8.0/` |
| Published executable | `publish/GSProBridge.exe` |
| Logs | `logs/` (when running published exe) |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Build/test failed or prerequisites missing |

Use exit codes for CI/CD automation.

## CI/CD Integration

### GitHub Actions Example

```yaml
- name: Build GSProBridge
  run: .\tools\build.ps1 -Clean -Test -Publish
  shell: pwsh
```

### Azure Pipelines Example

```yaml
- script: .\tools\build.ps1 -Clean -Test -Publish
  displayName: 'Build and Test'
```

## Troubleshooting

### "dotnet: command not found"
Install .NET SDK 8.0+ from https://dot.net/download

### "cannot be loaded because running scripts is disabled"
Run PowerShell as Administrator and execute:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

Or use `build.bat` which bypasses execution policy.

### Build fails with package restore errors
Check internet connection and NuGet.org accessibility:
```powershell
dotnet nuget list source
```

### Tests fail
Run tests individually to isolate failure:
```powershell
dotnet test src/GSProBridge.sln --logger "console;verbosity=detailed"
```

## Development Workflow

### First-time setup:
```powershell
git clone <repo>
cd gspro-bridge
.\tools\build.ps1 -Test
```

### Daily development:
```powershell
# Quick build (incremental)
.\tools\build.ps1 -Configuration Debug

# Full rebuild with tests
.\tools\build.ps1 -Clean -Test
```

### Pre-commit check:
```powershell
# Ensure Release build + tests pass
.\tools\build.ps1 -Clean -Test -Configuration Release
```

### Create distribution:
```powershell
.\tools\build.ps1 -Publish
# Output: publish/GSProBridge.exe (ready to share)
```

## File Descriptions

| File | Purpose |
|------|---------|
| `build.ps1` | Main PowerShell build script |
| `build.bat` | Simple CMD wrapper (calls build.ps1) |
| `README.md` | This file |

## Questions?

- Build issues: Check troubleshooting section above
- Feature requests: Open GitHub issue
- General help: See main repository README.md
