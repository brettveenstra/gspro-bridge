namespace GSProBridge.Models;

/// <summary>
/// Immutable snapshot of consolidated shot data.
/// Thread-safe for concurrent reads (no mutation after creation).
/// </summary>
public record ShotSnapshot
{
    public required InputSource Source { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required BallMetrics Ball { get; init; }
    public ClubMetrics? Club { get; init; }
}

/// <summary>
/// Ball flight metrics (consistent across R10 and webcam).
/// </summary>
public record BallMetrics
{
    public required double Speed { get; init; }              // MPH
    public required double HLA { get; init; }                 // Horizontal Launch Angle (degrees)
    public required double VLA { get; init; }                 // Vertical Launch Angle (degrees)
    public required double SpinAxis { get; init; }            // Spin axis tilt (degrees, -90 to +90)
    public required double TotalSpin { get; init; }           // RPM

    // Calculated fields (optional)
    public double? BackSpin { get; init; }                    // RPM
    public double? SideSpin { get; init; }                    // RPM
}

/// <summary>
/// Club metrics (only available from R10, not webcam).
/// </summary>
public record ClubMetrics
{
    public required double Speed { get; init; }               // Club head speed (MPH)
    public double? AngleOfAttack { get; init; }               // Degrees
    public double? FaceToTarget { get; init; }                // Degrees
    public double? Path { get; init; }                        // Degrees
}
