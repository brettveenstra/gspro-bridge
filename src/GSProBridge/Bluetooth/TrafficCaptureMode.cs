using Google.Protobuf;
using InTheHand.Bluetooth;
using LaunchMonitor.Proto;
using Microsoft.Extensions.Logging;

namespace GSProBridge.Bluetooth;

/// <summary>
/// Traffic capture mode: actively engages R10 with protocol handshake and captures bidirectional traffic
/// </summary>
public class TrafficCaptureMode
{
    private readonly ILogger<TrafficCaptureMode> _logger;
    private readonly string _outputFilePath;
    private readonly int _durationSeconds;
    private int _chunkCount;
    private readonly R10MessageCollector _messageCollector;

    // Forensic tracking
    private string _lastCommandSent = "None";
    private DateTime _lastCommandTime = DateTime.MinValue;
    private DateTime _lastPacketTime = DateTime.MinValue;

    // R10 GATT Service and Characteristics (confirmed via Gadgetbridge + community implementations)
    // DEVICE_INTERFACE service - for WakeUp/Subscribe commands
    private static readonly Guid _deviceInterfaceService = Guid.Parse("6A4E2800-667B-11E3-949A-0800200C9A66");
    private static readonly Guid _rxCharacteristic = Guid.Parse("6A4E2812-667B-11E3-949A-0800200C9A66"); // R10 → PC (notifications)
    private static readonly Guid _txCharacteristic = Guid.Parse("6A4E2822-667B-11E3-949A-0800200C9A66"); // PC → R10 (write)

    // MEASUREMENT service - for shot data and status
    private static readonly Guid _measurementService = Guid.Parse("6A4E3400-667B-11E3-949A-0800200C9A66");
    private static readonly Guid _measurementCharacteristic = Guid.Parse("6A4E3401-667B-11E3-949A-0800200C9A66"); // Shot data
    private static readonly Guid _statusCharacteristic = Guid.Parse("6A4E3403-667B-11E3-949A-0800200C9A66"); // Device status

    // Standard Bluetooth services (optional - may not be exposed by R10)
    private static readonly Guid _batteryService = Guid.Parse("0000180F-0000-1000-8000-00805F9B34FB"); // Battery Service
    private static readonly Guid _batteryLevelCharacteristic = Guid.Parse("00002A19-0000-1000-8000-00805F9B34FB"); // Battery Level
    private static readonly Guid _deviceInfoService = Guid.Parse("0000180A-0000-1000-8000-00805F9B34FB"); // Device Information
    private static readonly Guid _firmwareRevisionCharacteristic = Guid.Parse("00002A26-0000-1000-8000-00805F9B34FB"); // Firmware Revision

    /// <summary>
    /// Initializes a new instance of the <see cref="TrafficCaptureMode"/> class
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="messageCollector">Message collector for parsing R10 protocol messages</param>
    /// <param name="outputFilePath">Output file path for captured hex traffic</param>
    /// <param name="durationSeconds">Capture duration in seconds (default 60)</param>
    public TrafficCaptureMode(ILogger<TrafficCaptureMode> logger, R10MessageCollector messageCollector, string outputFilePath, int durationSeconds = 60)
    {
        _logger = logger;
        _messageCollector = messageCollector;
        _outputFilePath = outputFilePath;
        _durationSeconds = durationSeconds;
    }

    /// <summary>
    /// Run traffic capture: scan for paired R10, connect, capture chunks, write to file
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("R10 Traffic Capture Mode");
        _logger.LogInformation("Output file: {OutputFile}", _outputFilePath);
        _logger.LogInformation("Duration: {Duration} seconds", _durationSeconds);
        _logger.LogInformation("");

        // Phase 1: Find paired R10 device
        _logger.LogInformation("Scanning for paired R10 device...");
        BluetoothDevice? r10Device = await FindPairedR10Async();

        if (r10Device == null)
        {
            _logger.LogError("R10 device not found in paired devices.");
            _logger.LogError("Please pair R10 via Windows Bluetooth settings:");
            _logger.LogError("  1. Put R10 in pairing mode (hold power button → blue blinking light)");
            _logger.LogError("  2. Settings → Bluetooth & devices → Add device");
            _logger.LogError("  3. Select 'Approach R10'");
            return;
        }

        _logger.LogInformation("Found R10: {DeviceName} ({DeviceId})", r10Device.Name, r10Device.Id);
        _logger.LogInformation("");

        try
        {
            // Phase 2: Connect to R10
            _logger.LogInformation("Connecting to R10...");
            await r10Device.Gatt.ConnectAsync();

            if (!r10Device.Gatt.IsConnected)
            {
                _logger.LogError("Failed to connect to R10");
                return;
            }

            _logger.LogInformation("Connected to R10");
            _logger.LogInformation("");

            // Phase 3: Get GATT characteristics
            _logger.LogInformation("Getting GATT characteristics...");
            GattService deviceService = await r10Device.Gatt.GetPrimaryServiceAsync(_deviceInterfaceService);
            GattCharacteristic rxChar = await deviceService.GetCharacteristicAsync(_rxCharacteristic);
            GattCharacteristic txChar = await deviceService.GetCharacteristicAsync(_txCharacteristic);

            // Initialize output file with header
            InitializeOutputFile();

            // Subscribe to notifications
            await rxChar.StartNotificationsAsync();
            rxChar.CharacteristicValueChanged += OnChunkReceived;

            _logger.LogInformation("Starting protocol handshake...");
            _logger.LogInformation("");

            // Phase 4: Send WakeUp command (ACK-only, no protobuf response)
            _logger.LogInformation("Sending WakeUp command...");
            var wakeUpRequest = new WrapperProto
            {
                Service = new LaunchMonitorService
                {
                    WakeUpRequest = new WakeUpRequest()
                }
            };
            bool wakeUpAck = await SendRequestWithAckAsync(txChar, wakeUpRequest, "WakeUp", cancellationToken);

            if (!wakeUpAck)
            {
                _logger.LogWarning("WakeUp not acknowledged - continuing anyway");
            }

            await Task.Delay(500, cancellationToken); // Brief delay after WakeUp

            // Phase 4.5: Send StatusRequest command (ACK-only, for protocol capture)
            _logger.LogInformation("Sending StatusRequest for protocol capture...");
            var statusRequest = new WrapperProto
            {
                Service = new LaunchMonitorService
                {
                    StatusRequest = new StatusRequest()
                }
            };
            bool statusAck = await SendRequestWithAckAsync(txChar, statusRequest, "StatusRequest", cancellationToken);

            if (statusAck)
            {
                File.AppendAllText(_outputFilePath, $"# StatusRequest: ACK received{Environment.NewLine}");
            }
            else
            {
                File.AppendAllText(_outputFilePath, $"# StatusRequest: timeout (no ACK){Environment.NewLine}");
            }

            await Task.Delay(500, cancellationToken); // Brief delay after StatusRequest

            // Phase 4.6: Send TiltRequest command (ACK-only, for protocol capture)
            _logger.LogInformation("Sending TiltRequest for protocol capture...");
            var tiltRequest = new WrapperProto
            {
                Service = new LaunchMonitorService
                {
                    TiltRequest = new TiltRequest()
                }
            };
            bool tiltAck = await SendRequestWithAckAsync(txChar, tiltRequest, "TiltRequest", cancellationToken);

            if (tiltAck)
            {
                File.AppendAllText(_outputFilePath, $"# TiltRequest: ACK received{Environment.NewLine}");
            }
            else
            {
                File.AppendAllText(_outputFilePath, $"# TiltRequest: timeout (no ACK){Environment.NewLine}");
            }

            await Task.Delay(500, cancellationToken); // Brief delay after TiltRequest

            // Phase 5: Send Subscribe command (ACK + protobuf response)
            _logger.LogInformation("Sending Subscribe command...");
            var subscribeRequest = new WrapperProto
            {
                Event = new EventSharing
                {
                    SubscribeRequest = new SubscribeRequest
                    {
                        Alerts = { new AlertMessage { Type = AlertNotification.Types.AlertType.LaunchMonitor } }
                    }
                }
            };
            WrapperProto? subscribeResponse = await SendRequestWithResponseAsync(txChar, subscribeRequest, "Subscribe", cancellationToken);

            if (subscribeResponse != null)
            {
                _logger.LogInformation("Subscribe response received - R10 should be ready to measure");
                File.AppendAllText(_outputFilePath, $"# Subscribe Response: {subscribeResponse.GetType().Name}{Environment.NewLine}");
            }
            else
            {
                _logger.LogWarning("Subscribe response not received - manual R10 button press may be required");
            }

            await Task.Delay(500, cancellationToken); // Brief delay after Subscribe

            // Phase 6: Subscribe to MEASUREMENT service for shot data
            _logger.LogInformation("Subscribing to MEASUREMENT service for shot data...");
            GattService measurementService = await r10Device.Gatt.GetPrimaryServiceAsync(_measurementService);
            GattCharacteristic measurementChar = await measurementService.GetCharacteristicAsync(_measurementCharacteristic);
            GattCharacteristic statusChar = await measurementService.GetCharacteristicAsync(_statusCharacteristic);

            await measurementChar.StartNotificationsAsync();
            measurementChar.CharacteristicValueChanged += OnChunkReceived;

            await statusChar.StartNotificationsAsync();
            statusChar.CharacteristicValueChanged += OnChunkReceived;

            _logger.LogInformation("MEASUREMENT service subscribed (shot data + status)");
            _logger.LogInformation("");

            // Phase 6.5: Attempt to read optional device info (Battery, Firmware)
            // These are standard Bluetooth services that may or may not be exposed by R10
            _logger.LogInformation("Querying optional device info (battery, firmware)...");
            await TryReadBatteryLevelAsync(r10Device);
            await TryReadFirmwareVersionAsync(r10Device);
            _logger.LogInformation("");

            _logger.LogInformation("Handshake complete. Listening for shot data...");
            _logger.LogInformation("Capturing traffic for {Duration} seconds...", _durationSeconds);
            _logger.LogInformation("NOTE: Hit shots on R10 to capture shot data in protocol trace");
            _logger.LogInformation("NOTE: Sending periodic WakeUp commands to keep R10 active (prevent sleep)");
            _logger.LogInformation("");

            // Phase 7: Capture for specified duration with periodic keepalive
            // Send WakeUp every 10 seconds to prevent R10 from going to sleep (BLINKING WHITE)
            DateTime endTime = DateTime.UtcNow.AddSeconds(_durationSeconds);
            const int KeepaliveIntervalSeconds = 10;

            while (DateTime.UtcNow < endTime)
            {
                TimeSpan remaining = endTime - DateTime.UtcNow;
                int delaySeconds = Math.Min(KeepaliveIntervalSeconds, (int)remaining.TotalSeconds);

                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }

                // Send WakeUp keepalive if still within capture window
                if (DateTime.UtcNow < endTime)
                {
                    _logger.LogInformation("Sending keepalive WakeUp to maintain R10 active state...");
                    bool keepaliveAck = await SendRequestWithAckAsync(txChar, wakeUpRequest, "WakeUp (keepalive)", cancellationToken);
                    _ = keepaliveAck; // Ignore result for keepalive
                    await Task.Delay(500, cancellationToken); // Brief delay after keepalive
                }
            }

            // Cleanup
            rxChar.CharacteristicValueChanged -= OnChunkReceived;
            measurementChar.CharacteristicValueChanged -= OnChunkReceived;
            statusChar.CharacteristicValueChanged -= OnChunkReceived;
            FinalizeOutputFile();

            _logger.LogInformation("");
            _logger.LogInformation("Capture complete: {ChunkCount} chunks captured", _chunkCount);
            _logger.LogInformation("Output written to: {OutputFile}", _outputFilePath);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Capture stopped by user (Ctrl+C)");
            FinalizeOutputFile();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during traffic capture");
        }
    }

    private static async Task<BluetoothDevice?> FindPairedR10Async()
    {
        IReadOnlyCollection<BluetoothDevice> pairedDevices = await InTheHand.Bluetooth.Bluetooth.GetPairedDevicesAsync();

        foreach (BluetoothDevice device in pairedDevices)
        {
            // R10 device name contains "Approach" (exact name: "Approach R10")
            if (device.Name != null && device.Name.Contains("Approach", StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        return null;
    }

    private async Task SendProtobufMessageAsync(GattCharacteristic txChar, IMessage message, string commandName, CancellationToken cancellationToken)
    {
        // Forensic tracking: Record command being sent
        _lastCommandSent = commandName;
        _lastCommandTime = DateTime.UtcNow;

        // Frame and chunk the message using R10 protocol
        List<byte[]> chunks = R10Protocol.FrameAndChunkMessage(message);

        string timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        string logLine = $"[{timestamp}] TX {commandName} ({chunks.Count} chunks)";

        File.AppendAllText(_outputFilePath, logLine + Environment.NewLine);
        _logger.LogInformation(logLine);

        // Send and log each chunk
        for (int i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] chunk = chunks[i];
            string chunkTimestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            string chunkHex = R10Protocol.ToHexString(chunk);
            string chunkLog = $"[{chunkTimestamp}]   → Chunk {i + 1}/{chunks.Count} ({chunk.Length} bytes): {chunkHex}";

            File.AppendAllText(_outputFilePath, chunkLog + Environment.NewLine);
            _logger.LogInformation(chunkLog);

            await txChar.WriteValueWithResponseAsync(chunk);

            // Small delay between chunks
            if (i < chunks.Count - 1)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Send protobuf request and wait for ACK only (no protobuf response expected)
    /// Used for commands that only return acknowledgment: WakeUp, StatusRequest, TiltRequest
    /// </summary>
    private async Task<bool> SendRequestWithAckAsync(GattCharacteristic txChar, IMessage message, string commandName, CancellationToken cancellationToken)
    {
        // Clear any buffered messages before sending new request
        _messageCollector.Clear();

        // Send the request
        await SendProtobufMessageAsync(txChar, message, commandName, cancellationToken);

        // Wait for ACK (1 second timeout)
        R10Message? ack = await _messageCollector.WaitForMessageAsync(TimeSpan.FromSeconds(1), cancellationToken);

        if (ack?.Type == R10MessageType.Acknowledgment)
        {
            string timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            string logLine = $"[{timestamp}] ✓ {commandName} ACK received (counter: {ack.Counter})";
            File.AppendAllText(_outputFilePath, logLine + Environment.NewLine);
            _logger.LogInformation(logLine);
            return true;
        }

        string timeoutTimestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        string timeoutLog = $"[{timeoutTimestamp}] ⚠ {commandName} ACK timeout";
        File.AppendAllText(_outputFilePath, timeoutLog + Environment.NewLine);
        _logger.LogWarning(timeoutLog);
        return false;
    }

    /// <summary>
    /// Send protobuf request and wait for ACK + protobuf response
    /// Used for commands that return both ACK and protobuf data: Subscribe
    /// </summary>
    private async Task<WrapperProto?> SendRequestWithResponseAsync(GattCharacteristic txChar, IMessage message, string commandName, CancellationToken cancellationToken)
    {
        // Clear any buffered messages before sending new request
        _messageCollector.Clear();

        // Send the request
        await SendProtobufMessageAsync(txChar, message, commandName, cancellationToken);

        // Wait for ACK first (1 second timeout)
        R10Message? ack = await _messageCollector.WaitForMessageAsync(TimeSpan.FromSeconds(1), cancellationToken);

        if (ack?.Type != R10MessageType.Acknowledgment)
        {
            string ackTimestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            string ackLog = $"[{ackTimestamp}] ⚠ {commandName} ACK timeout";
            File.AppendAllText(_outputFilePath, ackLog + Environment.NewLine);
            _logger.LogWarning(ackLog);
            return null;
        }

        string ackReceivedTimestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        string ackReceivedLog = $"[{ackReceivedTimestamp}] ✓ {commandName} ACK received, waiting for protobuf response...";
        File.AppendAllText(_outputFilePath, ackReceivedLog + Environment.NewLine);
        _logger.LogDebug(ackReceivedLog);

        // Wait for protobuf response (5 second timeout)
        WrapperProto? response = await _messageCollector.WaitForProtobufResponseAsync(TimeSpan.FromSeconds(5), cancellationToken);

        if (response != null)
        {
            string responseTimestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            string responseLog = $"[{responseTimestamp}] ✓ {commandName} Response received: {response.GetType().Name}";
            File.AppendAllText(_outputFilePath, responseLog + Environment.NewLine);
            _logger.LogInformation(responseLog);
            return response;
        }

        string timeoutTimestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        string timeoutLog = $"[{timeoutTimestamp}] ⚠ {commandName} Response timeout (ACK received, no protobuf response)";
        File.AppendAllText(_outputFilePath, timeoutLog + Environment.NewLine);
        _logger.LogWarning(timeoutLog);
        return null;
    }

    private void OnChunkReceived(object? sender, GattCharacteristicValueChangedEventArgs e)
    {
        byte[]? chunk = e.Value;
        if (chunk == null || chunk.Length == 0)
        {
            return;
        }

        _chunkCount++;
        DateTime now = DateTime.UtcNow;

        string timestamp = now.ToString("HH:mm:ss.fff");
        string hex = R10Protocol.ToHexString(chunk);

        // === FORENSIC ANALYSIS ===
        string formatType = AnalyzeFormatType(chunk);
        string timingInfo = AnalyzeTimingContext(now);
        string asciiInfo = ExtractPrintableAscii(chunk);

        // Basic log
        string logLine = $"[{timestamp}] RX Chunk {_chunkCount:D4} ({chunk.Length} bytes): {hex}";
        File.AppendAllText(_outputFilePath, logLine + Environment.NewLine);
        _logger.LogInformation(logLine);

        // Forensic context log
        string forensicLog = $"  → Format: {formatType} | Context: After={_lastCommandSent} ({timingInfo}) | ASCII: {asciiInfo}";
        File.AppendAllText(_outputFilePath, forensicLog + Environment.NewLine);
        _logger.LogDebug(forensicLog);

        _lastPacketTime = now;

        // Feed chunk to message collector for message type discrimination and parsing
        _messageCollector.OnChunkReceived(chunk);
    }

    private static string AnalyzeFormatType(byte[] chunk)
    {
        // Detect format type for forensic analysis
        if (chunk.Length == 13 && chunk[0] == 0x00 && chunk[1] == 0x04)
        {
            byte statusCode = chunk[12];
            return $"13-byte-ACK (status=0x{statusCode:X2})";
        }

        if (chunk.Length >= 2 && chunk[0] == 0x00)
        {
            return "COBS-Start (0x00 delimiter)";
        }

        if (chunk.Length >= 1 && chunk[^1] == 0x00)
        {
            return "COBS-End (0x00 delimiter)";
        }

        if (chunk.Length <= 3)
        {
            return $"Short-Packet ({chunk.Length}B)";
        }

        return "Unknown-Format";
    }

    private string AnalyzeTimingContext(DateTime now)
    {
        if (_lastCommandTime == DateTime.MinValue)
        {
            return "no-cmd-yet";
        }

        TimeSpan sinceCommand = now - _lastCommandTime;
        TimeSpan sinceLastPacket = _lastPacketTime == DateTime.MinValue ? TimeSpan.Zero : now - _lastPacketTime;

        return $"+{sinceCommand.TotalMilliseconds:F0}ms-from-cmd, +{sinceLastPacket.TotalMilliseconds:F0}ms-from-prev";
    }

    private static string ExtractPrintableAscii(byte[] chunk)
    {
        string ascii = string.Concat(chunk.Where(b =>
        {
            return b >= 32 && b < 127;
        }).Select(b =>
        {
            return (char)b;
        }));
        return string.IsNullOrWhiteSpace(ascii) ? "(no-ascii)" : $"\"{ascii}\"";
    }

    private void InitializeOutputFile()
    {
        using StreamWriter writer = new(_outputFilePath, append: false);
        writer.WriteLine("# R10 Bluetooth Traffic Capture (Bidirectional)");
        writer.WriteLine($"# Captured: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        writer.WriteLine("# Format: [timestamp] TX/RX [CommandName] (length bytes): HEXDATA");
        writer.WriteLine("#   TX = PC → R10 (commands sent)");
        writer.WriteLine("#   RX = R10 → PC (notifications received)");
        writer.WriteLine();
    }

    private void FinalizeOutputFile()
    {
        File.AppendAllText(_outputFilePath, Environment.NewLine);
        File.AppendAllText(_outputFilePath, $"# Capture complete: {_chunkCount} chunks{Environment.NewLine}");
    }

    private async Task TryReadBatteryLevelAsync(BluetoothDevice device)
    {
        try
        {
            GattService batteryService = await device.Gatt.GetPrimaryServiceAsync(_batteryService);
            GattCharacteristic batteryChar = await batteryService.GetCharacteristicAsync(_batteryLevelCharacteristic);
            byte[]? batteryData = await batteryChar.ReadValueAsync();

            if (batteryData != null && batteryData.Length > 0)
            {
                int batteryLevel = batteryData[0]; // Battery level is 0-100%
                string logLine = $"Battery Level: {batteryLevel}%";
                _logger.LogInformation(logLine);
                File.AppendAllText(_outputFilePath, $"# {logLine}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Battery service not available: {Message}", ex.Message);
            File.AppendAllText(_outputFilePath, $"# Battery service: Not available{Environment.NewLine}");
        }
    }

    private async Task TryReadFirmwareVersionAsync(BluetoothDevice device)
    {
        try
        {
            GattService deviceInfoService = await device.Gatt.GetPrimaryServiceAsync(_deviceInfoService);
            GattCharacteristic firmwareChar = await deviceInfoService.GetCharacteristicAsync(_firmwareRevisionCharacteristic);
            byte[]? firmwareData = await firmwareChar.ReadValueAsync();

            if (firmwareData != null && firmwareData.Length > 0)
            {
                string firmwareVersion = System.Text.Encoding.UTF8.GetString(firmwareData);
                string logLine = $"Firmware Version: {firmwareVersion}";
                _logger.LogInformation(logLine);
                File.AppendAllText(_outputFilePath, $"# {logLine}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Device Information service not available: {Message}", ex.Message);
            File.AppendAllText(_outputFilePath, $"# Device Information service: Not available{Environment.NewLine}");
        }
    }
}
