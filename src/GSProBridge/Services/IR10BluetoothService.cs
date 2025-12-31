using LaunchMonitor.Proto;

namespace GSProBridge.Services;

/// <summary>
/// Interface for R10 Bluetooth connectivity and shot data streaming
/// </summary>
public interface IR10BluetoothService
{
    /// <summary>
    /// Event raised when shot data is received from R10
    /// </summary>
    event EventHandler<Metrics>? ShotDataReceived;

    /// <summary>
    /// Event raised when connection status changes
    /// </summary>
    event EventHandler<bool>? ConnectionStatusChanged;

    /// <summary>
    /// Connect to R10 device via Bluetooth
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Disconnect from R10 device
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Current connection status
    /// </summary>
    bool IsConnected
    {
        get;
    }
}
