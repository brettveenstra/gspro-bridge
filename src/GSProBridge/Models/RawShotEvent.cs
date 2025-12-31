namespace GSProBridge.Models;

/// <summary>
/// Raw shot event from Collection layer (before consolidation).
/// Adapters write these to Channels after device-level noise filtering.
/// </summary>
public record RawShotEvent
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
    /// Gets the shot snapshot data
    /// </summary>
    public required ShotSnapshot DomainData
    {
        get; init;
    }
}
