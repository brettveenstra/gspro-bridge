namespace GSProBridge.Services.R10;

/// <summary>
/// Event arguments for R10 device state changes.
/// </summary>
public class DeviceStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the new device state (STANDBY, WAITING, RECORDING, PROCESSING, ERROR).
    /// </summary>
    public object State
    {
        get; init;
    }

    /// <summary>
    /// Gets whether the device is ready to accept shots (true only when state is WAITING).
    /// </summary>
    public bool IsReady
    {
        get; init;
    }
    /// <inheritdoc/>

    public DeviceStateChangedEventArgs()
    {
        State = null!;
    }
}
