using System.Collections.Concurrent;
using System.Collections.Immutable;
using LaunchMonitor.Proto;
using Microsoft.Extensions.Logging;

namespace GSProBridge.Bluetooth;

/// <summary>
/// Collects COBS-encoded RX chunks from R10 and parses them into complete messages
/// Handles message boundary detection, COBS decoding, and protocol message parsing
/// Message boundaries: chunks start with 0x00, end with 0x00 (after BLE header byte)
/// NOTE: Raw 13-byte ACKs are NOT handled by this collector - see R10MessageFramer
/// </summary>
public class R10MessageCollector
{
    private readonly ILogger<R10MessageCollector> _logger;
    private readonly List<byte> _currentMessageData = new();
    private readonly ConcurrentQueue<R10Message> _parsedMessages = new();
    private readonly SemaphoreSlim _messageSemaphore = new(0);

    /// <summary>
    /// Creates a new R10MessageCollector with logging support
    /// </summary>
    /// <param name="logger">Logger for protocol analysis and debugging</param>
    public R10MessageCollector(ILogger<R10MessageCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <summary>
    /// Handles incoming RX chunk from R10
    /// COBS message format: 0x00 [COBS_DATA] 0x00 (delimiters)
    /// NOTE: No BLE header - chunks from GattCharacteristicValueChanged are raw protocol data
    /// </summary>
    /// <param name="chunk">Raw chunk bytes from BLE notification</param>
    public void OnChunkReceived(byte[] chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (chunk.Length == 0)
        {
            return;
        }

        // Check for new message start (chunk begins with 0x00 delimiter)
        if (chunk[0] == 0x00)
        {
            // Discard incomplete previous message
            _currentMessageData.Clear();
            // Remove start delimiter and use remaining data
            chunk = chunk.Skip(1).ToArray();
        }

        if (chunk.Length == 0)
        {
            return;
        }

        // Check for message completion (chunk ends with 0x00 delimiter)
        bool messageComplete = false;
        if (chunk[^1] == 0x00)
        {
            messageComplete = true;
            // Remove end delimiter
            chunk = chunk.SkipLast(1).ToArray();
        }

        // Accumulate chunk data
        _currentMessageData.AddRange(chunk);

        if (messageComplete && _currentMessageData.Count > 0)
        {
            // COBS decode + parse complete message
            R10Message? parsed = ParseMessage(_currentMessageData.ToArray());

            if (parsed != null)
            {
                _parsedMessages.Enqueue(parsed);
                int previousCount = _messageSemaphore.Release();
                _ = previousCount; // Discard
            }
            else
            {
                _logger.LogWarning("Failed to parse complete message ({ByteCount} bytes)", _currentMessageData.Count);
            }

            _currentMessageData.Clear();
        }
    }

    /// <summary>
    /// Parses COBS-encoded message into R10Message with type discrimination
    /// Handles COBS messages only: 0x13B3 (Request), 0x13B4 (Response)
    /// NOTE: Raw 13-byte ACKs (0x1388) are NOT handled here - see R10MessageFramer
    /// Includes defensive logging for protocol analysis and debugging
    /// </summary>
    /// <param name="cobsEncoded">COBS-encoded message bytes</param>
    /// <returns>Parsed R10Message or null if parsing failed</returns>
    private R10Message? ParseMessage(byte[] cobsEncoded)
    {
        try
        {
            // COBS decode
            byte[] framedMessage = CobsEncoding.Decode(cobsEncoded).ToArray();

            if (framedMessage.Length < 6)
            {
                return null; // Too short for valid frame
            }

            // Parse frame: [length:2] [protocol_message] [CRC:2]
            ushort frameLength = BitConverter.ToUInt16(framedMessage, 0);

            if (frameLength != framedMessage.Length)
            {
                return null; // Length mismatch
            }

            // Verify CRC16
            byte[] computedCrc = Crc16.ComputeChecksum(framedMessage.Take(framedMessage.Length - 2).ToArray());
            byte[] receivedCrc = framedMessage.Skip(framedMessage.Length - 2).Take(2).ToArray();

            if (!computedCrc.SequenceEqual(receivedCrc))
            {
                _logger.LogWarning(
                    "CRC failure - Computed: {ComputedCrc}, Received: {ReceivedCrc}",
                    BitConverter.ToString(computedCrc),
                    BitConverter.ToString(receivedCrc));
                return null; // CRC failure
            }

            // Extract protocol message (skip length, remove CRC)
            byte[] protocolMessage = framedMessage.Skip(2).Take(framedMessage.Length - 4).ToArray();

            if (protocolMessage.Length < 2)
            {
                return null; // Too short for protocol header
            }

            // Determine message type by protocol header (little-endian)
            ushort protocolHeader = BitConverter.ToUInt16(protocolMessage, 0);

            // Defensive logging: log unknown headers for protocol discovery
            // NOTE: 0x1388 ACKs are handled by R10MessageFramer (raw format, not COBS)
            if (protocolHeader != 0x13B4 && protocolHeader != 0x13B3)
            {
                _logger.LogWarning(
                    "UNKNOWN protocol header: 0x{Header:X4} - Message: {MessageHex}",
                    protocolHeader,
                    BitConverter.ToString(protocolMessage).Replace("-", ""));
            }

            return protocolHeader switch
            {
                0x13B4 => ParseProtobufResponse(protocolMessage),    // Response
                0x13B3 => ParseProtobufRequest(protocolMessage),     // Request from R10
                _ => new R10Message
                {
                    Type = R10MessageType.Unknown,
                    RawData = ImmutableArray.Create(protocolMessage)
                }
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses protobuf response message (protocol header: B4 13)
    /// Format: B4 13 [counter:2] 00 00 [length:4] [length:4] [protobuf]
    /// </summary>
    private static R10Message ParseProtobufResponse(byte[] protocolMessage)
    {
        if (protocolMessage.Length < 14)
        {
            return new R10Message
            {
                Type = R10MessageType.Unknown,
                RawData = ImmutableArray.Create(protocolMessage)
            };
        }

        ushort counter = BitConverter.ToUInt16(protocolMessage, 2);
        byte[] protobufBytes = protocolMessage.Skip(14).ToArray();

        WrapperProto? proto = null;
        try
        {
            proto = WrapperProto.Parser.ParseFrom(protobufBytes);
        }
        catch
        {
            // Protobuf parse failure - return as Unknown with raw data
        }

        return new R10Message
        {
            Type = R10MessageType.ProtobufResponse,
            Counter = counter,
            Protobuf = proto,
            RawData = ImmutableArray.Create(protobufBytes)
        };
    }

    /// <summary>
    /// Parses protobuf request message (protocol header: B3 13)
    /// Same format as protobuf response
    /// </summary>
    private static R10Message ParseProtobufRequest(byte[] protocolMessage)
    {
        R10Message response = ParseProtobufResponse(protocolMessage);
        return response with
        {
            Type = R10MessageType.ProtobufRequest
        };
    }

    /// <summary>
    /// Waits for next message (ACK or protobuf response or unknown)
    /// </summary>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Next message or null if timeout</returns>
    public async Task<R10Message?> WaitForMessageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        bool received = await _messageSemaphore.WaitAsync(timeout, cancellationToken);

        if (!received)
        {
            return null; // Timeout
        }

        // Semaphore acquired, try to dequeue message
        if (_parsedMessages.TryDequeue(out R10Message? message))
        {
            return message;
        }

        // Edge case: semaphore signaled but queue empty (shouldn't happen)
        _logger.LogWarning("Semaphore acquired but queue empty - unexpected state");
        return null;
    }

    /// <summary>
    /// Waits for next protobuf response (ignores ACKs and other message types)
    /// Useful when you expect a protobuf response after an ACK
    /// </summary>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Protobuf message or null if timeout</returns>
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
    /// Clears any buffered chunks and messages
    /// </summary>
    public void Clear()
    {
        _currentMessageData.Clear();

        while (_parsedMessages.TryDequeue(out R10Message? msg))
        {
            _ = msg; // Drain queue
        }

        // Reset semaphore count to 0
        while (_messageSemaphore.CurrentCount > 0)
        {
            bool waitResult = _messageSemaphore.Wait(0);
            _ = waitResult; // Discard result
        }
    }
}
