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

    // R10 GATT Service and Characteristics (confirmed via Gadgetbridge + community implementations)
    // DEVICE_INTERFACE service - for WakeUp/Subscribe commands
    private static readonly Guid _deviceInterfaceService = Guid.Parse("6A4E2800-667B-11E3-949A-0800200C9A66");
    private static readonly Guid _rxCharacteristic = Guid.Parse("6A4E2812-667B-11E3-949A-0800200C9A66"); // R10 → PC (notifications)
    private static readonly Guid _txCharacteristic = Guid.Parse("6A4E2822-667B-11E3-949A-0800200C9A66"); // PC → R10 (write)

    // MEASUREMENT service - for shot data and status
    private static readonly Guid _measurementService = Guid.Parse("6A4E3400-667B-11E3-949A-0800200C9A66");
    private static readonly Guid _measurementCharacteristic = Guid.Parse("6A4E3401-667B-11E3-949A-0800200C9A66"); // Shot data
    private static readonly Guid _statusCharacteristic = Guid.Parse("6A4E3403-667B-11E3-949A-0800200C9A66"); // Device status

    /// <summary>
    /// Initializes a new instance of the <see cref="TrafficCaptureMode"/> class
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="outputFilePath">Output file path for captured hex traffic</param>
    /// <param name="durationSeconds">Capture duration in seconds (default 60)</param>
    public TrafficCaptureMode(ILogger<TrafficCaptureMode> logger, string outputFilePath, int durationSeconds = 60)
    {
        _logger = logger;
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

            // Phase 4: Send WakeUp command
            _logger.LogInformation("Sending WakeUp command...");
            var wakeUpRequest = new WrapperProto
            {
                Service = new LaunchMonitorService
                {
                    WakeUpRequest = new WakeUpRequest()
                }
            };
            await SendProtobufMessageAsync(txChar, wakeUpRequest, "WakeUp", cancellationToken);
            await Task.Delay(1000, cancellationToken); // Wait for WakeUp response

            // Phase 5: Send Subscribe command
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
            await SendProtobufMessageAsync(txChar, subscribeRequest, "Subscribe", cancellationToken);
            await Task.Delay(1000, cancellationToken); // Wait for Subscribe response

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
                    await SendProtobufMessageAsync(txChar, wakeUpRequest, "WakeUp (keepalive)", cancellationToken);
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

    private void OnChunkReceived(object? sender, GattCharacteristicValueChangedEventArgs e)
    {
        byte[]? chunk = e.Value;
        if (chunk == null || chunk.Length == 0)
        {
            return;
        }

        _chunkCount++;

        string timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        string hex = R10Protocol.ToHexString(chunk);

        string logLine = $"[{timestamp}] RX Chunk {_chunkCount:D4} ({chunk.Length} bytes): {hex}";

        // Write to file AND console
        File.AppendAllText(_outputFilePath, logLine + Environment.NewLine);
        _logger.LogInformation(logLine);
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
}
