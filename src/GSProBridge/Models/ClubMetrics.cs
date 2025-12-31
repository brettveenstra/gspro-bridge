namespace GSProBridge.Models;

/// <summary>
/// Club metrics (only available from R10, not webcam).
/// </summary>
public record ClubMetrics
{
    /// <summary>Club head speed in MPH</summary>
    public required double Speed
    {
        get; init;
    }

    /// <summary>Angle of attack in degrees</summary>
    public double? AngleOfAttack
    {
        get; init;
    }

    /// <summary>Face angle to target in degrees</summary>
    public double? FaceToTarget
    {
        get; init;
    }

    /// <summary>Club path in degrees</summary>
    public double? Path
    {
        get; init;
    }
}
