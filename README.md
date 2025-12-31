# GSProBridge

A unified Windows .NET adapter for Garmin R10 launch monitors with webcam putting integration and GSPro golf simulator connectivity.

## Overview

GSProBridge provides a lightweight, transparent solution for connecting your Garmin R10 launch monitor and webcam putting setup to GSPro golf simulator software. This project aims to combine the best features from existing community solutions into a single, well-architected application.

## Features

- **Direct Bluetooth R10 Connection** - Native Windows Bluetooth connectivity to Garmin R10 launch monitor
- **E6 Connect API Server** - Acts as an E6 Connect compatible server for R10 integration
- **GSPro Open API Client** - Direct integration with GSPro using their Open API v1
- **Webcam Putting Integration** - Ball tracking for putting using computer vision (OpenCV)
- **Transparent Configuration** - Clear, documented settings with sensible defaults
- **Explicit C# Style** - Clean, readable code with explicit typing and verbose syntax

## Requirements

- **OS**: Windows 10/11 (required for Bluetooth LE support)
- **.NET**: .NET 8.0 or later
- **Hardware**:
  - Garmin R10 launch monitor
  - Bluetooth adapter (if not built-in)
  - Webcam (for putting integration, optional)
- **Software**: GSPro golf simulator

## Current Status

**⚠️ Early Development** - This project is currently in foundation/scaffolding phase. Core features are not yet implemented.

### Roadmap

- [ ] Solution architecture and project structure
- [ ] Garmin R10 Bluetooth protocol implementation
- [ ] E6 Connect HTTP server
- [ ] GSPro Open API client
- [ ] Webcam ball tracking integration
- [ ] Configuration management
- [ ] Windows desktop GUI
- [ ] Installer/deployment

## Installation

_Coming soon - project not yet ready for use_

## Building from Source

Requires .NET 8.0 SDK or later:

```bash
dotnet build
dotnet run --project src/GSProBridge
```

## Configuration

_Documentation coming soon_

## Contributing

This is an open-source project. Contributions, issues, and feature requests are welcome.

## Architecture

The project follows clean architecture principles with explicit separation of concerns:
- Domain models and interfaces
- Infrastructure implementations (Bluetooth, HTTP, computer vision)
- Application services and orchestration
- Presentation layer (console/GUI)

See `docs/architecture.md` (coming soon) for detailed design documentation.

## Credits

This project builds upon the work of the golf simulator community:
- [gsp-r10-adapter](https://github.com/mholow/gsp-r10-adapter) - R10 Bluetooth protocol reference
- [cam-putting-py](https://github.com/alleexx/cam-putting-py) - Webcam putting ball tracking
- GSPro community for API documentation and testing

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

Copyright (c) 2025 Brett Veenstra
