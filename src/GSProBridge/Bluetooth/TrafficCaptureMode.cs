using InTheHand.Bluetooth;
using Microsoft.Extensions.Logging;

namespace GSProBridge.Bluetooth;

/// <summary>
/// Traffic capture mode: passively captures raw R10 Bluetooth chunks to hex file
/// </summary>
public class TrafficCaptureMode
{
    private readonly ILogger<TrafficCaptureMode> _logger;
    private readonly string _outputFilePath;
    private readonly int _durationSeconds;
    private int _chunkCount;

    // R10 GATT Service and Characteristics (confirmed via Gadgetbridge + community implementations)
    private static readonly Guid _deviceInterfaceService = Guid.Parse("6A4E2800-667B-11E3-949A-0800200C9A66");
    private static readonly Guid _rxCharacteristic = Guid.Parse("6A4E2812-667B-11E3-949A-0800200C9A66"); // R10 → PC (notifications)

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

            // Phase 3: Subscribe to RX characteristic
            _logger.LogInformation("Subscribing to R10 RX characteristic...");
            GattService deviceService = await r10Device.Gatt.GetPrimaryServiceAsync(_deviceInterfaceService);
            GattCharacteristic rxChar = await deviceService.GetCharacteristicAsync(_rxCharacteristic);

            // Initialize output file with header
            InitializeOutputFile();

            // Subscribe to notifications
            await rxChar.StartNotificationsAsync();
            rxChar.CharacteristicValueChanged += OnChunkReceived;

            _logger.LogInformation("Capturing traffic for {Duration} seconds...", _durationSeconds);
            _logger.LogInformation("NOTE: R10 will NOT send data unless Garmin Golf app connects to it");
            _logger.LogInformation("      This tool LISTENS passively - start Garmin Golf app now");
            _logger.LogInformation("");

            // Phase 4: Capture for specified duration
            await Task.Delay(TimeSpan.FromSeconds(_durationSeconds), cancellationToken);

            // Cleanup
            rxChar.CharacteristicValueChanged -= OnChunkReceived;
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

        string logLine = $"[{timestamp}] Chunk {_chunkCount:D4} ({chunk.Length} bytes): {hex}";

        // Write to file AND console
        File.AppendAllText(_outputFilePath, logLine + Environment.NewLine);
        _logger.LogInformation(logLine);
    }

    private void InitializeOutputFile()
    {
        using StreamWriter writer = new(_outputFilePath, append: false);
        writer.WriteLine("# R10 Bluetooth Traffic Capture");
        writer.WriteLine($"# Captured: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        writer.WriteLine("# Format: [timestamp] Chunk #### (length bytes): HEXDATA");
        writer.WriteLine();
    }

    private void FinalizeOutputFile()
    {
        File.AppendAllText(_outputFilePath, Environment.NewLine);
        File.AppendAllText(_outputFilePath, $"# Capture complete: {_chunkCount} chunks{Environment.NewLine}");
    }
}
