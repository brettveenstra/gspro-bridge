using LaunchMonitor.Proto;

namespace GSProBridge.Bluetooth;

/// <summary>
/// Collects RX chunks and parses them into complete WrapperProto messages
/// Used for request-response pattern in R10 protocol
/// </summary>
public class ProtobufResponseCollector
{
    private readonly List<byte[]> _currentMessageChunks = new();
    private readonly Queue<WrapperProto> _parsedMessages = new();
    private readonly SemaphoreSlim _messageSemaphore = new(0);

    /// <summary>
    /// Handles incoming RX chunk from R10
    /// Collects chunks until complete message received (ends with 0x00), then parses
    /// </summary>
    /// <param name="chunk">Received chunk bytes</param>
    public void OnChunkReceived(byte[] chunk)
    {
        _currentMessageChunks.Add(chunk);

        // Check if this chunk completes the message (ends with 0x00 delimiter)
        if (chunk.Length > 0 && chunk[^1] == 0x00)
        {
            // Message complete - attempt to parse
            WrapperProto? parsed = R10Protocol.ParseReceivedMessage(_currentMessageChunks);

            if (parsed != null)
            {
                _parsedMessages.Enqueue(parsed);
                int previousCount = _messageSemaphore.Release();
                _ = previousCount; // Discard - we just need to signal
            }

            // Clear buffer for next message
            _currentMessageChunks.Clear();
        }
    }

    /// <summary>
    /// Waits for next complete WrapperProto message from R10
    /// </summary>
    /// <param name="timeout">Maximum time to wait for response</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed WrapperProto or null if timeout/cancellation</returns>
    public async Task<WrapperProto?> WaitForResponseAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        bool received = await _messageSemaphore.WaitAsync(timeout, cancellationToken);

        if (!received || _parsedMessages.Count == 0)
        {
            return null; // Timeout or no message
        }

        return _parsedMessages.Dequeue();
    }

    /// <summary>
    /// Clears any buffered chunks and messages
    /// </summary>
    public void Clear()
    {
        _currentMessageChunks.Clear();

        while (_parsedMessages.TryDequeue(out _))
        {
            // Drain queue
        }

        // Reset semaphore count to 0
        while (_messageSemaphore.CurrentCount > 0)
        {
            bool waitResult = _messageSemaphore.Wait(0);
            _ = waitResult; // Discard result - just draining semaphore
        }
    }
}
