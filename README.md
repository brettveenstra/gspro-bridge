# GSProBridge

**One unified bridge** for multiple launch monitor and putting devices to GSPro golf simulator.

## Why GSProBridge?

### Multi-Device Connection
Connect **multiple shot sources** through a single application:
- **Garmin R10** launch monitor (full swing)
- **Webcam** putting adapter (ball tracking)
- **Future devices** - architecture supports additional input sources

No more juggling multiple apps or manual coordination between devices.

### Transparent Configuration
- **Clear settings** - documented `appsettings.json` with sensible defaults
- **No hidden magic** - every configuration option explained
- **Environment overrides** - adjust settings without editing files

### Stability & Performance
- **Just works** - auto-reconnects when devices disconnect
- **Fast shot processing** - no lag between swing and simulator response
- **Won't crash** - handles multiple devices without freezing or errors
- **Runs in background** - minimal system resources, doesn't slow down your PC

### GSPro Compatibility
- **Works with GSPro** - directly connects to GSPro simulator
- **Smart mode switching** - automatically knows when you're putting vs full swing
- **Shot validation** - filters out bad readings before they reach the simulator

## Status

**⚠️ Early Development - Not Ready for Use**

Currently building the foundation (R10 → GSPro connection). Check back soon or watch this repo for updates.

## How It Works

GSProBridge runs on your PC and connects:
1. Your **R10 launch monitor** (via Bluetooth)
2. Your **webcam** (for putting, optional)
3. **GSPro simulator** (already running on your PC)

That's it. Hit a shot, it shows up in GSPro. No manual switching between apps.

## Requirements

- Windows 10/11
- GSPro golf simulator
- Garmin R10 launch monitor (paired via Windows Bluetooth settings)
- Webcam (optional, for putting)

## Installation

**Not available yet.** When ready:
1. Download latest release
2. Run GSProBridge.exe
3. Start GSPro
4. Hit shots

## Developer Documentation

See **[ARCHITECTURE.md](ARCHITECTURE.md)** for technical implementation details.

## Credits

This project builds upon the work of the golf simulator community:
- [gsp-r10-adapter](https://github.com/mholow/gsp-r10-adapter) - R10 Bluetooth protocol reference
- [cam-putting-py](https://github.com/alleexx/cam-putting-py) - Webcam putting ball tracking
- GSPro community for API documentation and testing

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

Copyright (c) 2025 Brett Veenstra
