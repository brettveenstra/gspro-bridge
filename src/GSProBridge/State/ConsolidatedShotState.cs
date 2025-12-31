using GSProBridge.Models;

namespace GSProBridge.State;

/// <summary>
/// Thread-safe state machine for consolidating shots from multiple sources.
/// Uses ReaderWriterLockSlim for multi-writer + high-frequency readonly access.
/// See: docs/architecture/decisions/ADR-006-readerwriterlockslim.md
/// </summary>
public class ConsolidatedShotState : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private ShotSnapshot? _currentSnapshot;
    private long _version;

    // Metrics (lock-free atomic counters)
    private long _eventsReceived;
    private long _eventsRejected;

    /// <summary>
    /// Update state with new shot event from Collection layer.
    /// Applies priority rules: R10 > webcam, staleness detection (5s threshold).
    /// </summary>
    public void Update(RawShotEvent evt)
    {
        _ = Interlocked.Increment(ref _eventsReceived);

        _lock.EnterWriteLock();
        try
        {
            if (ShouldAcceptEvent(evt))
            {
                _currentSnapshot = evt.DomainData;
                _ = Interlocked.Increment(ref _version);
            }
            else
            {
                _ = Interlocked.Increment(ref _eventsRejected);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Get current snapshot for Transmission layer (100Hz polling).
    /// Returns (snapshot, version) for efficient change detection.
    /// </summary>
    public (ShotSnapshot? snapshot, long version) GetCurrentSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return (_currentSnapshot, Interlocked.Read(ref _version));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Priority rules for multi-source consolidation.
    /// </summary>
    private bool ShouldAcceptEvent(RawShotEvent evt)
    {
        // Accept first shot
        if (_currentSnapshot == null)
        {
            return true;
        }

        // Priority: R10 > Webcam (R10 is more accurate)
        if (evt.Source == InputSource.R10 && _currentSnapshot.Source == InputSource.Webcam)
        {
            return true;
        }

        // Reject webcam if R10 data is fresh (<5s old)
        if (evt.Source == InputSource.Webcam && _currentSnapshot.Source == InputSource.R10)
        {
            TimeSpan age = DateTimeOffset.UtcNow - _currentSnapshot.Timestamp;
            if (age < TimeSpan.FromSeconds(5))
            {
                return false;  // R10 data still fresh, webcam can't override
            }
        }

        // Accept same-source updates
        return true;
    }

    /// <summary>
    /// Releases resources used by the consolidated shot state
    /// </summary>
    public void Dispose()
    {
        _lock?.Dispose();
    }
}
