using System.Collections.Concurrent;
using System.Collections.Immutable;
using LaunchMonitor.Proto;
using Microsoft.Extensions.Logging;

namespace GSProBridge.Bluetooth;

/// <summary>
/// Message framer for R10 dual-format protocol
/// Detects message format at chunk boundaries and routes to appropriate handler
/// Handles two distinct formats:
/// 1. COBS-encoded messages (echoes, shot data, responses) with 0x00 delimiters
/// 2. Raw 13-byte ACKs (command acknowledgments) with NO COBS encoding
/// </summary>
public class R10MessageFramer
{
    private readonly ILogger<R10MessageFramer> _logger;
    private readonly R10MessageCollector _cobsCollector;
    private readonly ConcurrentQueue<R10Message> _messageQueue = new();
    private readonly SemaphoreSlim _messageSemaphore = new(0);

    /// <summary>
    /// Event raised when a complete R10 message is ready (either raw ACK or COBS message)
    /// </summary>
    public event EventHandler<R10Message>? OnMessageReady;

    /// <summary>
    /// Creates a new R10MessageFramer with COBS message collector
    /// </summary>
    /// <param name="logger">Logger for format detection and routing</param>
    /// <param name="cobsCollector">COBS message collector for multi-chunk assembly</param>
    public R10MessageFramer(ILogger<R10MessageFramer> logger, R10MessageCollector cobsCollector)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(cobsCollector);

        _logger = logger;
        _cobsCollector = cobsCollector;
    }

    /// <summary>
    /// Handles incoming RX chunk from R10
    /// Detects format type at chunk boundary and routes appropriately
    /// NOTE: Chunks from GattCharacteristicValueChanged are raw protocol data (no BLE header)
    /// </summary>
    /// <param name="chunk">Raw chunk bytes from BLE notification</param>
    public void OnChunkReceived(byte[] chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (chunk.Length == 0)
        {
            return;
        }

        // Detect raw ACK format at chunk boundary
        if (IsRawAck(chunk))
        {
            EmitRawAck(chunk);
        }
        else
        {
            // Route to COBS message collector for multi-chunk assembly
            _cobsCollector.OnChunkReceived(chunk);
        }
    }

    /// <summary>
    /// Detects raw 13-byte ACK format
    /// ACK signature: length=13, starts with 0x00 0x04
    /// NOTE: No BLE header - chunks from GattCharacteristicValueChanged are raw protocol data
    /// </summary>
    /// <param name="chunk">Raw chunk bytes from BLE notification</param>
    /// <returns>True if chunk is a raw ACK, false otherwise</returns>
    private static bool IsRawAck(byte[] chunk)
    {
        // Raw ACK format: [00 04] [10 padding bytes] [status:1]
        // Total: 13 bytes
        if (chunk.Length != 13)
        {
            return false;
        }

        // Check for ACK signature 0x00 0x04 at start of chunk
        if (chunk[0] != 0x00 || chunk[1] != 0x04)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Parses raw 13-byte ACK and emits R10Message event
    /// Raw ACKs have no COBS encoding, no counter, no protobuf payload
    /// Format: [00 04] [10 padding bytes] [status:1]
    /// Status code at byte[12]: 0x02 (generic ACK), 0x01 (Subscribe ACK)
    /// </summary>
    /// <param name="chunk">Raw 13-byte ACK chunk (no BLE header)</param>
    private void EmitRawAck(byte[] chunk)
    {
        // Status code is at byte[12] (last byte of 13-byte chunk)
        byte statusCode = chunk[12];

        _logger.LogDebug(
            "Raw ACK detected: status=0x{StatusCode:X2}",
            statusCode);

        R10Message ack = new()
        {
            Type = R10MessageType.Acknowledgment,
            Counter = 0, // Raw ACKs don't have counter field
            Protobuf = null, // Raw ACKs don't have protobuf payload
            RawData = ImmutableArray.Create(chunk)
        };

        // Queue for WaitForMessageAsync() consumers
        _messageQueue.Enqueue(ack);
        int previousCount = _messageSemaphore.Release();
        _ = previousCount; // Discard

        // Emit event for event-based consumers
        OnMessageReady?.Invoke(this, ack);
    }

    /// <summary>
    /// Waits for next message (either raw ACK or COBS message)
    /// Checks framer's queue first (raw ACKs), then polls COBS collector
    /// </summary>
    /// <param name="timeout">Maximum time to wait for message</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>R10Message or null if timeout</returns>
    public async Task<R10Message?> WaitForMessageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Check if we have queued raw ACKs first (non-blocking dequeue)
        if (_messageQueue.TryDequeue(out R10Message? message))
        {
            // Drain semaphore to keep queue/semaphore in sync (non-blocking)
            _ = _messageSemaphore.Wait(0);
            return message;
        }

        // Try to wait for semaphore signal (from raw ACKs)
        bool acquired = await _messageSemaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
        if (acquired && _messageQueue.TryDequeue(out message))
        {
            return message;
        }

        // No raw ACKs available, poll COBS collector for COBS messages
        return await _cobsCollector.WaitForMessageAsync(timeout, cancellationToken);
    }

    /// <summary>
    /// Waits for next protobuf response message, ignoring ACKs
    /// Polls WaitForMessageAsync until ProtobufResponse received or timeout
    /// </summary>
    /// <param name="timeout">Maximum time to wait for protobuf response</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>WrapperProto protobuf message or null if timeout</returns>
    public async Task<WrapperProto?> WaitForProtobufResponseAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;

            if (remaining <= TimeSpan.Zero)
            {
                return null; // Timeout
            }

            R10Message? msg = await WaitForMessageAsync(remaining, cancellationToken);

            if (msg == null)
            {
                return null; // Timeout
            }

            if (msg.Type == R10MessageType.ProtobufResponse)
            {
                return msg.Protobuf;
            }

            // Ignore ACKs and continue waiting for protobuf response
        }

        return null; // Timeout
    }

    /// <summary>
    /// Clears all pending messages (both raw ACKs and COBS messages)
    /// </summary>
    public void Clear()
    {
        _messageQueue.Clear();

        // Drain semaphore
        while (_messageSemaphore.CurrentCount > 0)
        {
            bool drained = _messageSemaphore.Wait(0);
            _ = drained; // Discard
        }

        _cobsCollector.Clear();
    }
}
