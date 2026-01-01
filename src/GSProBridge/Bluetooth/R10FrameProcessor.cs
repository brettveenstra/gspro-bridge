using System.Diagnostics;
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
    private readonly Stopwatch _timeSinceLastMessage = Stopwatch.StartNew();

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

        long timeSinceLastMs = _timeSinceLastMessage.ElapsedMilliseconds;
        _timeSinceLastMessage.Restart();

        _logger.LogDebug("RX chunk ({Length} bytes, {TimeSince}ms since last): {Hex}",
            chunk.Length, timeSinceLastMs, R10Protocol.ToHexString(chunk));

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

        // If starting new frame and we have accumulated data, process it first
        if (frameStart && _currentFrame.Count > 0)
        {
            _logger.LogDebug("New frame starting - processing accumulated {Length} bytes first", _currentFrame.Count);
            ProcessCompleteFrame(_currentFrame.ToArray());
            _currentFrame.Clear();
        }

        // Start new frame
        if (frameStart)
        {
            _currentFrame.Clear();
        }

        // Accumulate frame bytes
        _currentFrame.AddRange(payload);

        // Process complete frame when delimiter detected
        if (frameEnd && _currentFrame.Count > 0)
        {
            ProcessCompleteFrame(_currentFrame.ToArray());
            _currentFrame.Clear();
        }
        // CRITICAL: Also process single-chunk messages without delimiters
        // Short responses (like WakeUp/Subscribe ACKs) fit in one chunk and lack 0x00 delimiters
        else if (!frameStart && !frameEnd && chunk.Length < 19 && _currentFrame.Count > 0)
        {
            _logger.LogDebug("Short single-chunk message detected ({Length} bytes) - processing immediately", _currentFrame.Count);
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

                // Log detailed wrapper contents (verbose mode)
                LogWrapperDetails(wrapper);

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

    /// <summary>
    /// Logs detailed contents of WrapperProto message for verbose debugging
    /// </summary>
    /// <param name="wrapper">Parsed WrapperProto message</param>
    private void LogWrapperDetails(WrapperProto wrapper)
    {
        _logger.LogDebug("=== WrapperProto Message Details ===");

        // Service field (responses to our commands)
        if (wrapper.Service != null)
        {
            _logger.LogDebug("  Service field present:");

            if (wrapper.Service.WakeUpResponse != null)
            {
                _logger.LogDebug("    - WakeUpResponse: Status={Status}", wrapper.Service.WakeUpResponse.Status);
            }

            // Log any other service responses we discover
            _logger.LogDebug("    - Service wrapper present (check protobuf for other response types)");
        }

        // Event field (notifications, alerts, state changes)
        if (wrapper.Event != null)
        {
            _logger.LogDebug("  Event field present:");

            if (wrapper.Event.SubscribeRespose != null)
            {
                _logger.LogDebug("    - SubscribeResponse: (subscription confirmed)");
            }

            if (wrapper.Event.Notification != null)
            {
                _logger.LogDebug("    - Notification field present:");

                if (wrapper.Event.Notification.AlertNotification_ != null)
                {
                    AlertDetails alert = wrapper.Event.Notification.AlertNotification_;
                    _logger.LogDebug("      - AlertNotification:");

                    // Metrics (shot data)
                    if (alert.Metrics != null)
                    {
                        _logger.LogDebug("        - SHOT DATA! Shot ID: {ShotId}", alert.Metrics.ShotId);

                        if (alert.Metrics.BallMetrics != null)
                        {
                            BallMetrics ball = alert.Metrics.BallMetrics;
                            _logger.LogDebug("          Ball: Speed={Speed:F2} m/s, Launch={LaunchAngle:F1}°, Direction={LaunchDirection:F1}°, Spin={TotalSpin:F0} RPM",
                                ball.BallSpeed, ball.LaunchAngle, ball.LaunchDirection, ball.TotalSpin);
                        }

                        if (alert.Metrics.ClubMetrics != null)
                        {
                            ClubMetrics club = alert.Metrics.ClubMetrics;
                            _logger.LogDebug("          Club: Speed={Speed:F2} m/s, Path={Path:F1}°, Face={Face:F1}°, Attack={Attack:F1}°",
                                club.ClubHeadSpeed, club.ClubAnglePath, club.ClubAngleFace, club.AttackAngle);
                        }
                    }

                    // State changes
                    if (alert.State != null)
                    {
                        _logger.LogDebug("        - State change: {State}", alert.State.State_);
                    }

                    // Errors
                    if (alert.Error != null)
                    {
                        _logger.LogWarning("        - ERROR from R10: Code={Code}, Severity={Severity}",
                            alert.Error.Code, alert.Error.Severity);
                    }
                }
            }
        }

        _logger.LogDebug("=== End WrapperProto Details ===");
    }
}
