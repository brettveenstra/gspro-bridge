namespace GSProBridge.Services.R10;

/// <summary>
/// Event arguments for R10 device errors.
/// </summary>
public class DeviceErrorEventArgs : EventArgs
{
    /// <summary>
    /// Gets the error code (OVERHEATING, RADAR_SATURATION, PLATFORM_TILTED).
    /// </summary>
    public object ErrorCode
    {
        get; init;
    }

    /// <summary>
    /// Gets the error severity (WARNING, SERIOUS, FATAL).
    /// </summary>
    public object Severity
    {
        get; init;
    }

    /// <summary>
    /// Gets the error message describing the issue.
    /// </summary>
    public string Message
    {
        get; init;
    }
    /// <inheritdoc/>

    public DeviceErrorEventArgs()
    {
        ErrorCode = null!;
        Severity = null!;
        Message = string.Empty;
    }
}
