using Google.Protobuf;
using InTheHand.Bluetooth;
using LaunchMonitor.Proto;
using Microsoft.Extensions.Logging;
using BluetoothProtocol = GSProBridge.Bluetooth;

namespace GSProBridge.Services;

/// <summary>
/// Garmin R10 Bluetooth connectivity service
/// </summary>
public class R10BluetoothService : IR10BluetoothService
{
    // R10 BLE Service UUIDs
    private static readonly Guid _measurementServiceUuid = Guid.Parse("6A4E3400-667B-11E3-949A-0800200C9A66");
    private static readonly Guid _controlPointCharacteristicUuid = Guid.Parse("6A4E3402-667B-11E3-949A-0800200C9A66");

    private readonly ILogger<R10BluetoothService> _logger;
    private readonly BluetoothProtocol.R10FrameProcessor _frameProcessor;
    private BluetoothDevice? _device;
    private GattCharacteristic? _controlPointCharacteristic;
    private bool _isConnected;

    /// <summary>
    /// Event raised when shot data is received from R10
    /// </summary>
    public event EventHandler<Metrics>? ShotDataReceived;

    /// <summary>
    /// Event raised when connection status changes
    /// </summary>
    public event EventHandler<bool>? ConnectionStatusChanged;

    /// <summary>
    /// Gets the current connection status
    /// </summary>
    public bool IsConnected
    {
        get
        {
            return _isConnected;
        }
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                ConnectionStatusChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="R10BluetoothService"/> class
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public R10BluetoothService(ILogger<R10BluetoothService> logger)
    {
        _logger = logger;
        _frameProcessor = new BluetoothProtocol.R10FrameProcessor(logger);
        _frameProcessor.MessageReceived += OnFrameProcessorMessageReceived;
    }

    /// <summary>
    /// Connects to R10 device via Bluetooth LE
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Searching for Approach R10 in paired devices...");

            // Find R10 in paired devices
            _device = await FindR10DeviceAsync(cancellationToken);
            if (_device == null)
            {
                _logger.LogError("Approach R10 not found in paired devices. Please pair via Windows Bluetooth settings.");
                return;
            }

            _logger.LogInformation("Found R10: {DeviceId}", _device.Id);
            _logger.LogInformation("Connecting to R10...");

            // Connect to GATT server
            cancellationToken.ThrowIfCancellationRequested();
            await _device.Gatt.ConnectAsync();

            if (!_device.Gatt.IsConnected)
            {
                _logger.LogError("Failed to connect to R10 GATT server");
                return;
            }

            _logger.LogInformation("Connected to R10 GATT server");

            // Subscribe to notifications
            await SetupNotificationsAsync(cancellationToken);

            IsConnected = true;
            _logger.LogInformation("R10 setup complete - ready to receive shot data");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("R10 connection cancelled");
            IsConnected = false;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to R10");
            IsConnected = false;
            throw;
        }
    }

    /// <summary>
    /// Disconnects from R10 device
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_controlPointCharacteristic != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _controlPointCharacteristic.StopNotificationsAsync();
            }

            _device = null;
            _controlPointCharacteristic = null;
            IsConnected = false;

            _logger.LogInformation("Disconnected from R10");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("R10 disconnection cancelled");
            throw;
        }
    }

    private async Task<BluetoothDevice?> FindR10DeviceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyCollection<BluetoothDevice> pairedDevices = await InTheHand.Bluetooth.Bluetooth.GetPairedDevicesAsync();

        foreach (BluetoothDevice device in pairedDevices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Found paired device: {DeviceName}", device.Name);

            if (device.Name?.Contains("Approach R10") == true)
            {
                return device;
            }
        }

        return null;
    }

    private async Task SetupNotificationsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Setting up BLE notifications...");

        cancellationToken.ThrowIfCancellationRequested();

        // Get measurement service
        GattService measService = await _device!.Gatt.GetPrimaryServiceAsync(_measurementServiceUuid) ?? throw new InvalidOperationException("Failed to get measurement service");

        cancellationToken.ThrowIfCancellationRequested();

        // Get control point characteristic (this is where protobuf messages arrive)
        _controlPointCharacteristic = await measService.GetCharacteristicAsync(_controlPointCharacteristicUuid);
        if (_controlPointCharacteristic == null)
        {
            throw new InvalidOperationException("Failed to get control point characteristic");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Subscribe to notifications
        _controlPointCharacteristic.CharacteristicValueChanged += OnCharacteristicValueChanged;
        await _controlPointCharacteristic.StartNotificationsAsync();

        _logger.LogDebug("Subscribed to R10 notifications");

        // Send activation commands to put R10 into shot detection mode
        await ActivateR10Async(cancellationToken);
    }

    private async Task ActivateR10Async(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Activating R10 shot detection mode...");

        cancellationToken.ThrowIfCancellationRequested();

        // Send WakeUp request
        var wakeUpRequest = new WrapperProto
        {
            Service = new LaunchMonitorService
            {
                WakeUpRequest = new WakeUpRequest()
            }
        };

        await SendProtobufMessageAsync(wakeUpRequest, cancellationToken);
        _logger.LogDebug("Sent WakeUp request to R10");

        cancellationToken.ThrowIfCancellationRequested();

        // Send SubscribeRequest to activate shot detection (CRITICAL for GREEN LED)
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

        await SendProtobufMessageAsync(subscribeRequest, cancellationToken);
        _logger.LogInformation("Sent SubscribeRequest - R10 should now be in shot detection mode (GREEN LED)");
    }

    /// <summary>
    /// Sends a protobuf message to R10 using proper protocol framing and chunking
    /// </summary>
    /// <param name="message">Protobuf message to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task SendProtobufMessageAsync(IMessage message, CancellationToken cancellationToken)
    {
        // Frame and chunk the message using R10 protocol
        List<byte[]> chunks = BluetoothProtocol.R10Protocol.FrameAndChunkMessage(message);

        _logger.LogDebug("Sending protobuf message in {ChunkCount} chunks", chunks.Count);

        // Send each chunk sequentially
        for (int i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] chunk = chunks[i];
            _logger.LogDebug("Sending chunk {ChunkIndex}/{ChunkCount}: {ChunkHex}",
                i + 1, chunks.Count, BluetoothProtocol.R10Protocol.ToHexString(chunk));

            await _controlPointCharacteristic!.WriteValueWithResponseAsync(chunk);

            // Small delay between chunks to avoid overwhelming the R10
            if (i < chunks.Count - 1)
            {
                await Task.Delay(10, cancellationToken);
            }
        }

        _logger.LogDebug("Successfully sent all {ChunkCount} chunks", chunks.Count);
    }

    private void OnCharacteristicValueChanged(object? sender, GattCharacteristicValueChangedEventArgs e)
    {
        try
        {
            if (e.Value == null || e.Value.Length == 0)
            {
                return;
            }

            // Pass chunk to frame processor for accumulation, decoding, and parsing
            _frameProcessor.ProcessChunk(e.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing R10 BLE chunk");
        }
    }

    private void OnFrameProcessorMessageReceived(object? sender, WrapperProto wrapper)
    {
        try
        {
            // Check if this is a shot data notification
            if (wrapper.Event?.Notification?.AlertNotification_ != null)
            {
                AlertDetails alertDetails = wrapper.Event.Notification.AlertNotification_;

                if (alertDetails.Metrics != null)
                {
                    _logger.LogInformation("Shot data received! Shot ID: {ShotId}", alertDetails.Metrics.ShotId);

                    // Raise event with shot data
                    ShotDataReceived?.Invoke(this, alertDetails.Metrics);
                }

                // Log state changes
                if (alertDetails.State != null)
                {
                    _logger.LogDebug("R10 state: {State}", alertDetails.State.State_);
                }

                // Log errors
                if (alertDetails.Error != null)
                {
                    _logger.LogWarning("R10 error: {ErrorCode} ({Severity})",
                        alertDetails.Error.Code,
                        alertDetails.Error.Severity);
                }
            }

            // Check if this is a response to our commands (WakeUp, Subscribe)
            if (wrapper.Service != null)
            {
                if (wrapper.Service.WakeUpResponse != null)
                {
                    _logger.LogInformation("R10 WakeUp response: {Status}", wrapper.Service.WakeUpResponse.Status);
                }

                if (wrapper.Event?.SubscribeRespose != null)
                {
                    _logger.LogInformation("R10 Subscribe response received");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing R10 message");
        }
    }
}
