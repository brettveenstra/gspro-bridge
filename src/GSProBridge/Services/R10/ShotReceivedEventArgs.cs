namespace GSProBridge.Services.R10;

/// <summary>
/// Event arguments for shot data received from R10 device.
/// </summary>
public class ShotReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the shot metrics from the R10 (ball speed, HLA, VLA, spin, club data).
    /// </summary>
    public object Metrics
    {
        get; init;
    }

    /// <summary>
    /// Gets the unique shot ID assigned by the R10 device.
    /// </summary>
    public uint ShotId
    {
        get; init;
    }
    /// <inheritdoc/>

    public ShotReceivedEventArgs()
    {
        Metrics = null!;
    }
}
