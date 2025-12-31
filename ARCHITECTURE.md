# GSProBridge Architecture

**Technical documentation for developers and contributors.**

This document explains the implementation approach, design decisions, and technical patterns used in GSProBridge. For user-facing information, see [README.md](README.md).

---

## Implementation Status

**Current**: Foundation scaffold (Models, State, Configuration)
**Next**: Collection layer (R10 Bluetooth adapter), Transmission layer (GSPro TCP client)
**Future**: Webcam integration, mode coordination, comprehensive testing

This document describes both current implementation and planned architecture. Sections marked **[Future]** describe components not yet implemented.

---

## Table of Contents

- [Design Philosophy](#design-philosophy)
- [Architecture Overview](#architecture-overview)
- [Core Components](#core-components)
- [Concurrency & Threading](#concurrency--threading)
- [Resilience Patterns](#resilience-patterns)
- [Performance Characteristics](#performance-characteristics)
- [Protocol Integration](#protocol-integration) [Future]
- [Configuration System](#configuration-system)
- [Testing Strategy](#testing-strategy) [Future]
- [Build & Development](#build--development)

---

## Design Philosophy

### Core Principles

**Reliability Over Features**: Connection stability and shot accuracy are non-negotiable. Features are added only when they don't compromise these fundamentals.

**Clear Over Clever**: Explicit, readable code patterns over "clever" optimizations. Future maintainers (including your future self) will thank you.

**Measured Performance**: Optimize where it matters. Latency budgets are measured, not assumed. Premature optimization is avoided.

**Transparent Configuration**: Every setting documented, sensible defaults provided. No hidden magic that breaks when users need to customize.

### Architecture Goals

1. **Multi-device consolidation** - Single application handles R10 + webcam + future devices
2. **Sub-100ms shot latency** - From device read to GSPro submission
3. **Connection resilience** - Automatic reconnection with exponential backoff
4. **Thread safety** - Concurrent device inputs without data races
5. **Simple deployment** - Single .exe, minimal configuration required

---

## Architecture Overview

### Collection-Transmission-State Pattern

GSProBridge uses a **3-layer async pipeline** to handle variable-rate device inputs and fixed-rate simulator transmission:

```
┌─────────────────────┐
│  Collection Layer   │  Variable-rate device inputs
│  (R10 + Webcam)     │  • R10: ~0.3 Hz (per swing)
└──────────┬──────────┘  • Webcam: 60 FPS (continuous)
           │
           │ RawShotEvent (Channels)
           ▼
┌─────────────────────┐
│   State Machine     │  Multi-writer consolidation
│  (ConsolidatedShot) │  • Priority: R10 > webcam
└──────────┬──────────┘  • Staleness: 5s threshold
           │
           │ ShotSnapshot polling
           ▼
┌─────────────────────┐
│ Transmission Layer  │  Fixed-rate polling
│   (GSPro Client)    │  • 100 Hz change detection
└─────────────────────┘  • Version-based optimization
```

**Why this pattern?**

**Impedance matching**: Devices produce data at vastly different rates. The state machine acts as a buffer/consolidator, allowing the transmission layer to poll at a consistent rate regardless of device timing.

**Separation of concerns**: Device adapters focus on protocol parsing. State machine handles multi-source logic. Transmission handles API resilience.

**Testability**: Each layer can be tested independently with mock implementations.

---

## Core Components

### Models (Domain Layer)

**`InputSource` enum** - Device type identifier (R10, Webcam)

**`ShotSnapshot` record** - Immutable snapshot of shot data
- Uses C# records for structural equality and immutability
- Contains `BallMetrics` (speed, launch angles, spin) and optional `ClubMetrics`
- Thread-safe by design (no mutable state)

**`RawShotEvent` record** - Collection layer output
- Wrapper around `ShotSnapshot` with source + timestamp metadata
- Written to `Channel<RawShotEvent>` after device-level noise filtering

### State Machine (`ConsolidatedShotState`)

**Purpose**: Thread-safe consolidation of shots from multiple sources

**Concurrency model**: `ReaderWriterLockSlim` (ADR-006)
- Multi-writer: R10 adapter + webcam adapter can write concurrently
- High-frequency readers: Transmission layer polls at 100Hz
- Lock-free atomic counters for metrics (`_eventsReceived`, `_eventsRejected`)

**Priority rules**:
```csharp
// R10 always overrides webcam (more accurate)
if (evt.Source == InputSource.R10 && _currentSnapshot.Source == InputSource.Webcam)
    return true;  // Accept R10

// Webcam can't override fresh R10 data (<5s old)
if (evt.Source == InputSource.Webcam && _currentSnapshot.Source == InputSource.R10)
{
    var age = DateTimeOffset.UtcNow - _currentSnapshot.Timestamp;
    if (age < TimeSpan.FromSeconds(5))
        return false;  // Reject webcam, R10 still fresh
}
```

**Version-based change detection**: Transmission layer only submits when `_version` increments (avoids redundant GSPro API calls)

---

## Concurrency & Threading

### Threading Model

**Collection Layer** (per-device async loops)
- R10 Bluetooth: `BluetoothLEDevice` async read loop
- Webcam: OpenCV frame processing loop (future)
- Each writes to shared `Channel<RawShotEvent>`

**State Machine** (synchronized access)
- Writers: `EnterWriteLock()` for snapshot updates
- Readers: `EnterReadLock()` for polling (100Hz from transmission layer)
- Lock-free: Atomic counters via `Interlocked` operations

**Transmission Layer** (single async loop)
- 100Hz polling timer (`PeriodicTimer`)
- Version-based change detection (only act on new snapshots)
- Polly-wrapped GSPro TCP client

### Why `ReaderWriterLockSlim`?

**Multi-writer scenario**: Both R10 and webcam can produce shots concurrently. Standard `lock` or `SemaphoreSlim` would serialize all access.

**Read-heavy workload**: Transmission polls at 100Hz, writes happen at ~0.3-60Hz. `ReaderWriterLockSlim` allows concurrent readers when no writes are occurring.

**Performance**: Measured overhead <1ms for typical lock acquisition (ADR-006 benchmarks)

**Alternative considered**: `System.Threading.Channels` for lock-free queuing, but rejected because:
- Transmission needs "current state" (not queue of historical events)
- Priority logic requires comparing new events against current snapshot
- State machine is conceptually a single mutable cell, not a queue

---

## Resilience Patterns

### Polly v8 Integration

All external I/O (GSPro TCP, R10 Bluetooth reconnection) uses Polly resilience pipelines:

**Retry Policy** (transient failures)
```csharp
ConnectionRetry: {
  InitialDelay: "00:00:01",  // 1s first retry
  MaxDelay: "00:00:30",      // Cap at 30s
  BackoffType: "Exponential" // 1s, 2s, 4s, 8s, 16s, 30s, 30s...
}
```

**Circuit Breaker** (cascading failures)
```csharp
CircuitBreaker: {
  FailureRatio: 0.5,           // Open after 50% failure rate
  SamplingDuration: "00:00:10", // Over 10s window
  MinimumThroughput: 5,        // Need 5 requests to evaluate
  BreakDuration: "00:00:30"    // Stay open 30s before retry
}
```

**Timeout Policy** (hung connections)
```csharp
Timeout: {
  SendTimeout: "00:00:05"  // 5s max for GSPro API call
}
```

### Device Reconnection

**R10 Bluetooth**:
- `ConnectionStatusChanged` event triggers reconnection attempt
- Exponential backoff (5s, 10s, 20s, 40s, capped at 60s)
- User notification after 3 consecutive failures
- Manual reconnect button always available

**GSPro TCP**:
- Detect disconnect via socket errors or heartbeat timeout
- Circuit breaker prevents reconnect spam during GSPro shutdown
- Automatic retry when circuit closes

---

## Performance Characteristics

### Latency Budget (Target: <100ms end-to-end)

| Stage | Budget | Notes |
|-------|--------|-------|
| R10 Bluetooth read | <20ms | BLE notify event processing |
| Device-level filtering | <5ms | Range checks, incomplete data rejection |
| Channel write | <1ms | Bounded channel, backpressure handling |
| State machine update | <1ms | Lock acquisition + snapshot replacement |
| Transmission polling | <10ms | Version check (no-op if unchanged) |
| GSPro API submission | <50ms | TCP send + ACK (localhost) |
| **Total** | **<100ms** | Measured via Stopwatch in debug logs |

### Memory Efficiency

**Immutable snapshots**: `ShotSnapshot` records are small (~200 bytes) and short-lived (replaced on each shot)

**No unbounded queues**: `Channel<RawShotEvent>` has bounded capacity (max 10 pending), prevents memory growth during backpressure

**Pooling** (future optimization): `ArrayPool<byte>` for Bluetooth read buffers, `MemoryPool<T>` for frame processing

### Garbage Collection Impact

**Low allocation rate**: Steady state produces ~1 snapshot per shot (~0.3-1 Hz for R10, 60 Hz for webcam)

**Gen0 collections**: Webcam frame processing (future) will drive Gen0 collections, but snapshots are small enough to stay in Gen0

**No LOH allocations**: All data structures <85KB (Large Object Heap threshold)

---

## Protocol Integration [Future]

### R10 Bluetooth (Direct Connection)

**Why Direct Bluetooth?**
- Skips Garmin Golf app dependency (E6 Connect server)
- Access to additional metrics (Angle of Attack, swing tempo)
- Proven viable by [gsp-r10-adapter](https://github.com/mholow/gsp-r10-adapter)

**Protocol**: BLE GATT + Google Protobuf
- Service UUID: `6A4E3400-667B-11E3-949A-0800200C9A66`
- Measurement characteristic: `6A4E3401-...`
- Control point characteristic: `6A4E3402-...`

**Library**: `InTheHand.BluetoothLE` v4.0.37 (cross-platform BLE for .NET)

**Shot data**:
```protobuf
message ShotDataProto {
  optional double ballSpeed = 1;        // MPH
  optional double totalSpin = 2;        // RPM
  optional double hla = 3;              // Horizontal launch angle (degrees)
  optional double vla = 4;              // Vertical launch angle (degrees)
  optional double clubSpeed = 5;        // MPH
  optional double clubPath = 6;         // Degrees
  optional double attackAngle = 7;      // Degrees (R10 only, not in E6 Connect)
  optional double tempo = 8;            // Backswing/downswing ratio
}
```

**Device-level noise filtering** (ADR-005):
- Reject incomplete shots (missing ball speed)
- Reject implausible values (speed >220 MPH, spin >12000 RPM)
- Reject before writing to Channel (don't pollute state machine)

### GSPro Open Connect API v1

**Protocol**: TCP socket (localhost:921), newline-delimited JSON

**Bidirectional**:
- **Outbound**: Shot data, heartbeats (10s interval)
- **Inbound**: Server responses, player state (club selection)

**Mode coordination**:
```json
{
  "Player": {
    "Club": "PT"  // Putting mode when Club == "PT"
  }
}
```

When GSPro signals putting mode, transmission layer:
1. Filters out R10 shots (full swing not expected)
2. Prioritizes webcam shots (ball tracking)
3. Sends `BallData` only (no `ClubData` for putts)

**Connection management**:
- Single persistent socket (reused across shots)
- Heartbeat every 10s (keep-alive)
- Detect disconnect via socket errors or heartbeat timeout
- Reconnect with Polly retry policy

---

## Configuration System

### `appsettings.json` Structure

**GSPro Section**:
```json
{
  "GSPro": {
    "Host": "127.0.0.1",     // Usually localhost
    "Port": 921,              // Default GSPro Open Connect port
    "DeviceID": "GSPRO-R10-BRIDGE",
    "HeartbeatIntervalSeconds": 10,
    "ConnectionRetry": { /* Polly retry config */ },
    "CircuitBreaker": { /* Polly circuit breaker config */ },
    "Timeout": { /* Polly timeout config */ }
  }
}
```

**R10 Bluetooth Section**:
```json
{
  "R10Bluetooth": {
    "DeviceName": "Approach R10",        // BLE device name filter
    "AutoWake": true,                     // Send wake command on connect
    "CalibrateTiltOnConnect": true,       // Tilt calibration request
    "DebugLogging": false,                // Verbose BLE logging
    "SendStatusToGSPro": false,           // R10 status messages (battery, etc.)
    "ReconnectIntervalSeconds": 5,        // Initial backoff delay
    "Environment": {
      "Temperature": 60,                  // Fahrenheit
      "Humidity": 0.5,                    // 0.0-1.0 (50%)
      "Altitude": 0,                      // Feet above sea level
      "AirDensity": 1.0,                  // Relative (1.0 = sea level)
      "TeeDistanceFeet": 7                // R10 to tee distance
    }
  }
}
```

**Webcam Section** (future):
```json
{
  "Webcam": {
    "Enabled": false,
    "DeviceIndex": 0,
    "HSV": {
      "HMin": 3, "HMax": 20,       // Orange ball hue range
      "SMin": 181, "SMax": 255,    // Saturation
      "VMin": 134, "VMax": 255     // Value (brightness)
    }
  }
}
```

**Serilog Section**:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",   // Suppress noisy framework logs
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": { "restrictedToMinimumLevel": "Information" }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/gspro-bridge-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "restrictedToMinimumLevel": "Debug"
        }
      }
    ]
  }
}
```

### Environment Variable Overrides

All settings can be overridden via environment variables using standard .NET configuration binding:

```bash
# Override GSPro port
GSPro__Port=9999

# Override R10 device name
R10Bluetooth__DeviceName="My Custom R10"

# Enable debug logging
R10Bluetooth__DebugLogging=true
```

---

## Testing Strategy [Future]

### Unit Tests

**State machine testing**:
- Priority logic (R10 > webcam)
- Staleness detection (5s threshold)
- Version increment behavior
- Thread safety (concurrent read/write stress tests)

**Shot validation**:
- Range checks (speed, spin, angles)
- Incomplete data rejection
- Boundary conditions (exactly 220 MPH, exactly 12000 RPM)

### Integration Tests

**R10 Bluetooth** (requires real device or mock BLE server):
- Connection/disconnection cycles
- Protobuf message parsing
- Device-level noise filtering
- Reconnection backoff timing

**GSPro TCP client** (mock server):
- Shot submission format
- Heartbeat timing
- Reconnection on disconnect
- Circuit breaker behavior

### Manual Testing Checklist

- [ ] R10 pairs via Windows Bluetooth settings
- [ ] GSProBridge auto-connects to paired R10
- [ ] Shots appear in GSPro within <100ms
- [ ] R10 power-off → auto-reconnect works
- [ ] GSPro close → app handles gracefully
- [ ] Putting mode → webcam shots prioritized
- [ ] Multiple consecutive shots (no hangs)
- [ ] 30-minute session (stability test)

---

## Build & Development

### Prerequisites

- .NET 8 SDK or later
- Windows 10/11 (Bluetooth LE APIs)
- Visual Studio 2022 or VS Code + C# DevKit

### Build Commands

```bash
# Restore dependencies
dotnet restore

# Build (Debug)
dotnet build

# Build (Release)
dotnet build -c Release

# Run
dotnet run

# Run tests
dotnet test

# Publish single-file exe
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Project Structure

**Current**:
```
src/
└── GSProBridge/
    ├── Models/               # Domain models (InputSource, ShotSnapshot, RawShotEvent)
    ├── State/                # State machine (ConsolidatedShotState)
    ├── appsettings.json      # Configuration
    └── GSProBridge.csproj    # Dependencies
```

**Planned** [Future]:
```
src/
├── GSProBridge/
│   ├── Collection/           # Device adapters (R10Adapter, WebcamAdapter)
│   ├── Transmission/         # GSPro client (GSProTcpClient)
│   ├── Services/             # Cross-cutting (ModeCoordinator)
│   └── Program.cs            # Entry point + DI setup
└── GSProBridge.Tests/        # Unit + integration tests
```

### Coding Standards

- **File-scoped namespaces** (`namespace GSProBridge.Models;`)
- **Explicit braces** (no expression-bodied members)
- **Warnings as errors** (all CA* warnings must be resolved)
- **Async/await** for all I/O (no blocking calls on UI thread)
- **Immutable domain models** (C# records, `required` + `init`)
- **Dependency injection** (constructor injection, no service locator)

---

## References

### External Resources

- [InTheHand.BluetoothLE Documentation](https://github.com/inthehand/32feet) - Cross-platform Bluetooth library
- [Polly v8 Documentation](https://www.pollydocs.org/) - Resilience patterns
- [gsp-r10-adapter](https://github.com/mholow/gsp-r10-adapter) - R10 Bluetooth protocol reference
- [cam-putting-py](https://github.com/alleexx/cam-putting-py) - Webcam ball tracking reference
- [GSPro Open Connect API Documentation](https://gsprogolf.com) - GSPro integration guide

---

**Questions?** Open an issue on GitHub or check existing discussions.
