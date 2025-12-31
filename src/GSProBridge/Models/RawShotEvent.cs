namespace GSProBridge.Models;

/// <summary>
/// Raw shot event from Collection layer (before consolidation).
/// Adapters write these to Channels after device-level noise filtering.
/// </summary>
public record RawShotEvent
{
    public required InputSource Source { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required ShotSnapshot DomainData { get; init; }
}
