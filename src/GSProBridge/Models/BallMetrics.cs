namespace GSProBridge.Models;

/// <summary>
/// Ball flight metrics (consistent across R10 and webcam).
/// </summary>
public record BallMetrics
{
    /// <summary>Ball speed in MPH</summary>
    public required double Speed
    {
        get; init;
    }

    /// <summary>Horizontal Launch Angle in degrees</summary>
    public required double HLA
    {
        get; init;
    }

    /// <summary>Vertical Launch Angle in degrees</summary>
    public required double VLA
    {
        get; init;
    }

    /// <summary>Spin axis tilt in degrees (-90 to +90)</summary>
    public required double SpinAxis
    {
        get; init;
    }

    /// <summary>Total spin in RPM</summary>
    public required double TotalSpin
    {
        get; init;
    }

    /// <summary>Back spin in RPM (calculated)</summary>
    public double? BackSpin
    {
        get; init;
    }

    /// <summary>Side spin in RPM (calculated)</summary>
    public double? SideSpin
    {
        get; init;
    }
}
