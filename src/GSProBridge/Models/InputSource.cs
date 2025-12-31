namespace GSProBridge.Models;

/// <summary>
/// Identifies the source device for shot data.
/// </summary>
public enum InputSource
{
    /// <summary>
    /// Garmin R10 launch monitor (Bluetooth connection).
    /// </summary>
    R10,

    /// <summary>
    /// Webcam putting adapter (OpenCV ball tracking).
    /// </summary>
    Webcam
}
