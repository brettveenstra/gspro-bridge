namespace GSProBridge.Services.R10;

/// <summary>
/// R10 measurement service channel for receiving shot notification data from the Garmin R10 launch monitor.
/// Provides events for shot data, device state changes, and errors.
/// </summary>
#pragma warning disable CA1812 // Class is instantiated via dependency injection (WIP: not yet wired up)
internal class R10MeasurementChannel
{
    /// <summary>
    /// Fired when shot data (Metrics) is received and validated from the R10 device.
    /// </summary>
    public event EventHandler<ShotReceivedEventArgs>? ShotReceived;

    /// <summary>
    /// Fired when device state changes (STANDBY, WAITING, RECORDING, PROCESSING, ERROR).
    /// </summary>
    public event EventHandler<DeviceStateChangedEventArgs>? DeviceStateChanged;

    /// <summary>
    /// Fired when the R10 reports an error (OVERHEATING, RADAR_SATURATION, PLATFORM_TILTED).
    /// </summary>
    public event EventHandler<DeviceErrorEventArgs>? DeviceError;
}
