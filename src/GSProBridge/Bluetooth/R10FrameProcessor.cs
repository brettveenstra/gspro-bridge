using LaunchMonitor.Proto;
using Microsoft.Extensions.Logging;

namespace GSProBridge.Bluetooth;

/// <summary>
/// Processes incoming R10 BLE frames: accumulates chunks, COBS decodes, validates CRC, parses protobuf
/// </summary>
public class R10FrameProcessor
{
    private readonly ILogger _logger;
    private readonly List<byte> _currentFrame = new();
    private bool _handshakeComplete;

    /// <summary>
    /// Event raised when a complete WrapperProto message is received
    /// </summary>
    public event EventHandler<WrapperProto>? MessageReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="R10FrameProcessor"/> class
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public R10FrameProcessor(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes an incoming BLE chunk from R10
    /// </summary>
    /// <param name="chunk">Raw chunk bytes (19 bytes or less)</param>
    public void ProcessChunk(byte[] chunk)
    {
        if (chunk.Length == 0)
        {
            return;
        }

        _logger.LogDebug("RX chunk ({Length} bytes): {Hex}", chunk.Length, R10Protocol.ToHexString(chunk));

        // First byte is header (used in handshake)
        byte header = chunk[0];
        byte[] payload = chunk.Skip(1).ToArray();

        // Handle handshake (skip for now - measurement service doesn't require handshake)
        if (!_handshakeComplete && header == 0x00)
        {
            _logger.LogDebug("Handshake chunk detected (skipping - not needed for measurement service)");
            _handshakeComplete = true;
            return;
        }

        // Detect frame boundaries
        bool frameStart = false;
        bool frameEnd = false;

        if (payload.Length > 0 && payload[0] == 0x00)
        {
            // Start delimiter
            frameStart = true;
            payload = payload.Skip(1).ToArray();
            _logger.LogDebug("Frame START detected");
        }

        if (payload.Length > 0 && payload[^1] == 0x00)
        {
            // End delimiter
            frameEnd = true;
            payload = payload.SkipLast(1).ToArray();
            _logger.LogDebug("Frame END detected");
        }

        // Start new frame
        if (frameStart)
        {
            _currentFrame.Clear();
        }

        // Accumulate frame bytes
        _currentFrame.AddRange(payload);

        // Process complete frame
        if (frameEnd && _currentFrame.Count > 0)
        {
            ProcessCompleteFrame(_currentFrame.ToArray());
            _currentFrame.Clear();
        }
    }

    private void ProcessCompleteFrame(byte[] encodedFrame)
    {
        _logger.LogDebug("Complete frame received ({Length} bytes COBS-encoded): {Hex}",
            encodedFrame.Length, R10Protocol.ToHexString(encodedFrame));

        try
        {
            // COBS decode
            byte[] decodedFrame = CobsEncoding.Decode(encodedFrame).ToArray();
            _logger.LogDebug("COBS decoded ({Length} bytes): {Hex}",
                decodedFrame.Length, R10Protocol.ToHexString(decodedFrame));

            if (decodedFrame.Length < 4)
            {
                _logger.LogWarning("Frame too short ({Length} bytes) - expected at least 4 bytes", decodedFrame.Length);
                return;
            }

            // Validate CRC-16 (last 2 bytes)
            byte[] frameWithoutCrc = decodedFrame.SkipLast(2).ToArray();
            byte[] expectedCrc = Crc16.ComputeChecksum(frameWithoutCrc);
            byte[] actualCrc = decodedFrame.TakeLast(2).ToArray();

            if (!expectedCrc.SequenceEqual(actualCrc))
            {
                _logger.LogError("CRC validation failed! Expected: {Expected}, Actual: {Actual}",
                    R10Protocol.ToHexString(expectedCrc), R10Protocol.ToHexString(actualCrc));
                return;
            }

            _logger.LogDebug("CRC validation PASSED");

            // Extract message payload: skip length prefix (2 bytes), skip CRC (last 2 bytes)
            byte[] message = decodedFrame.Skip(2).SkipLast(2).ToArray();

            if (message.Length < 16)
            {
                _logger.LogWarning("Message too short ({Length} bytes) - expected at least 16 bytes", message.Length);
                return;
            }

            // Check message type
            string messageHex = R10Protocol.ToHexString(message);
            _logger.LogDebug("Message type: {Type}", messageHex.Substring(0, 4));

            if (messageHex.StartsWith("B313") || messageHex.StartsWith("B413"))
            {
                // B313 = Protobuf request/notification from R10 (shot data, state changes)
                // B413 = Protobuf response from R10 (ack to our commands)

                // Extract protobuf payload: skip header (16 bytes)
                byte[] protobufPayload = message.Skip(16).ToArray();

                _logger.LogDebug("Parsing protobuf ({Length} bytes): {Hex}",
                    protobufPayload.Length, R10Protocol.ToHexString(protobufPayload));

                // Parse WrapperProto
                WrapperProto wrapper = WrapperProto.Parser.ParseFrom(protobufPayload);

                _logger.LogDebug("Protobuf parsed successfully");

                // Raise event
                MessageReceived?.Invoke(this, wrapper);
            }
            else
            {
                _logger.LogDebug("Unknown message type: {Type} - ignoring", messageHex.Substring(0, 4));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing frame");
        }
    }
}
