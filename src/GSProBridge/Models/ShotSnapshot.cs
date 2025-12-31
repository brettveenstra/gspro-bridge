namespace GSProBridge.Models;

/// <summary>
/// Immutable snapshot of consolidated shot data.
/// Thread-safe for concurrent reads (no mutation after creation).
/// </summary>
public record ShotSnapshot
{
    /// <summary>
    /// Gets the source of the shot data (R10 or webcam)
    /// </summary>
    public required InputSource Source
    {
        get; init;
    }

    /// <summary>
    /// Gets the timestamp when the shot was captured
    /// </summary>
    public required DateTimeOffset Timestamp
    {
        get; init;
    }

    /// <summary>
    /// Gets the ball flight metrics
    /// </summary>
    public required BallMetrics Ball
    {
        get; init;
    }

    /// <summary>
    /// Gets the club metrics (null for webcam putting shots)
    /// </summary>
    public ClubMetrics? Club
    {
        get; init;
    }
}
