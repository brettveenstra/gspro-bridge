using GSProBridge.Bluetooth;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace GSProBridge.Tests.Bluetooth;

/// <summary>
/// Tests for R10MessageFramer - validates dual-format detection and routing
/// Tests raw 13-byte ACK detection and COBS message passthrough
/// </summary>
[TestFixture]
public class R10MessageFramerTests
{
    private R10MessageFramer _framer = null!;
    private R10MessageCollector _collector = null!;
    private ILogger<R10MessageFramer> _framerLogger = null!;
    private ILogger<R10MessageCollector> _collectorLogger = null!;

    [SetUp]
    public void SetUp()
    {
        // Create test loggers (NullLogger for tests - doesn't output anything)
        _framerLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<R10MessageFramer>();
        _collectorLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<R10MessageCollector>();

        // Create collector and framer (framer depends on collector for COBS messages)
        _collector = new R10MessageCollector(_collectorLogger);
        _framer = new R10MessageFramer(_framerLogger, _collector);
    }

    /// <summary>
    /// Tests raw 13-byte ACK detection with generic status code 0x02
    /// This is the format observed in forensic analysis for WakeUp, StatusRequest, TiltRequest
    /// </summary>
    [Test]
    public async Task OnChunkReceived_Raw13ByteAckGeneric_DetectedAndParsed()
    {
        // Arrange - Raw 13-byte ACK (NO COBS encoding, NO delimiters, NO BLE header)
        // Format: [00 04] [10 padding bytes] [status:1]
        byte[] rawAckChunk = new byte[]
        {
            0x00, 0x04,                                                        // ACK signature
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,        // 10 padding bytes
            0x02                                                                // Status: generic ACK
        };

        // Act
        _framer.OnChunkReceived(rawAckChunk);
        R10Message? message = await _framer.WaitForMessageAsync(TimeSpan.FromMilliseconds(100));

        // Assert
        _ = message.ShouldNotBeNull();
        message.Type.ShouldBe(R10MessageType.Acknowledgment);
        message.Counter.ShouldBe((ushort)0); // Raw ACKs don't have counter
        message.Protobuf.ShouldBeNull(); // Raw ACKs don't have protobuf
        message.RawData.Length.ShouldBe(13); // Should preserve raw chunk
        message.RawData[12].ShouldBe((byte)0x02); // Status code at byte[12]
    }

    /// <summary>
    /// Tests raw 13-byte ACK detection with Subscribe status code 0x01
    /// This format observed in forensic analysis specifically after Subscribe command
    /// </summary>
    [Test]
    public async Task OnChunkReceived_Raw13ByteAckSubscribe_DetectedAndParsed()
    {
        // Arrange - Raw 13-byte ACK with Subscribe-specific status
        byte[] rawAckChunk = new byte[]
        {
            0x00, 0x04,                                                        // ACK signature
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,        // 10 padding bytes
            0x01                                                                // Status: Subscribe ACK
        };

        // Act
        _framer.OnChunkReceived(rawAckChunk);
        R10Message? message = await _framer.WaitForMessageAsync(TimeSpan.FromMilliseconds(100));

        // Assert
        _ = message.ShouldNotBeNull();
        message.Type.ShouldBe(R10MessageType.Acknowledgment);
        message.RawData[12].ShouldBe((byte)0x01); // Subscribe status code
    }

    /// <summary>
    /// Tests that COBS-encoded messages are routed to collector (not treated as ACKs)
    /// Validates framer's format detection doesn't misidentify COBS messages
    /// </summary>
    [Test]
    public async Task OnChunkReceived_CobsMessage_PassedThroughToCollector()
    {
        // Arrange - COBS-encoded message (echo or response)
        // Build a simple COBS message with unknown header FF FF
        byte[] protocolMessage = new byte[]
        {
            0xFF, 0xFF, // Unknown protocol header (for testing passthrough)
            0x01, 0x00  // Some data
        };

        ushort frameLength = (ushort)(2 + protocolMessage.Length + 2);
        byte[] lengthBytes = BitConverter.GetBytes(frameLength);

        List<byte> frame = [];
        frame.AddRange(lengthBytes);
        frame.AddRange(protocolMessage);

        byte[] crcData = frame.ToArray();
        byte[] crc = Crc16.ComputeChecksum(crcData);
        frame.AddRange(crc);

        byte[] cobsEncoded = CobsEncoding.Encode(frame.ToArray()).ToArray();

        List<byte> chunk = [0x00]; // Start delimiter (no BLE header)
        chunk.AddRange(cobsEncoded);
        chunk.Add(0x00); // End delimiter

        // Act
        _framer.OnChunkReceived(chunk.ToArray());
        R10Message? message = await _framer.WaitForMessageAsync(TimeSpan.FromMilliseconds(100));

        // Assert - Should get message from collector (passthrough successful)
        _ = message.ShouldNotBeNull();
        message.Type.ShouldBe(R10MessageType.Unknown); // Unknown header, but parsed
    }

    /// <summary>
    /// Tests edge case: chunk with wrong length (not 13 bytes) should NOT be treated as raw ACK
    /// Should be routed to COBS collector for normal processing
    /// </summary>
    [Test]
    public async Task OnChunkReceived_WrongLength_NotDetectedAsRawAck()
    {
        // Arrange - 14-byte chunk starting with 0x00 0x04 (but wrong length for raw ACK)
        byte[] chunk = new byte[14];
        chunk[0] = 0x00; // Could be ACK signature or COBS start delimiter
        chunk[1] = 0x04; // Coincidentally matches ACK header

        // Act
        _framer.OnChunkReceived(chunk);

        // This should timeout since it's not a valid COBS message either
        R10Message? message = await _framer.WaitForMessageAsync(TimeSpan.FromMilliseconds(50));

        // Assert - Should timeout (not detected as raw ACK, and not valid COBS)
        message.ShouldBeNull();
    }

    /// <summary>
    /// Tests edge case: 13-byte chunk with wrong header should NOT be detected as raw ACK
    /// </summary>
    [Test]
    public async Task OnChunkReceived_WrongHeader_NotDetectedAsRawAck()
    {
        // Arrange - 13-byte chunk but wrong header (0x00 0x05 instead of 0x00 0x04)
        byte[] chunk = new byte[]
        {
            0x00, 0x05,                                                        // WRONG ACK signature
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,        // 10 padding bytes
            0x02                                                                // Status
        };

        // Act
        _framer.OnChunkReceived(chunk);

        // Should timeout (not detected as raw ACK)
        R10Message? message = await _framer.WaitForMessageAsync(TimeSpan.FromMilliseconds(50));

        // Assert
        message.ShouldBeNull();
    }

    /// <summary>
    /// Tests edge case: empty chunk should not crash
    /// </summary>
    [Test]
    public async Task OnChunkReceived_EmptyChunk_HandledGracefully()
    {
        // Arrange
        byte[] emptyChunk = [];

        // Act
        _framer.OnChunkReceived(emptyChunk);
        R10Message? message = await _framer.WaitForMessageAsync(TimeSpan.FromMilliseconds(50));

        // Assert - Should timeout gracefully (not crash)
        message.ShouldBeNull();
    }

    /// <summary>
    /// Tests mixed format sequence: COBS echo → raw ACK
    /// This is the pattern observed in ALL commands during forensic analysis
    /// </summary>
    [Test]
    public async Task OnChunkReceived_MixedSequence_BothFormatsDetected()
    {
        // Arrange - First a COBS message (echo)
        byte[] protocolMessage = new byte[]
        {
            0xFF, 0xFF, // Unknown header
            0xAA, 0xBB  // Test data
        };

        ushort frameLength = (ushort)(2 + protocolMessage.Length + 2);
        byte[] lengthBytes = BitConverter.GetBytes(frameLength);

        List<byte> frame = [];
        frame.AddRange(lengthBytes);
        frame.AddRange(protocolMessage);

        byte[] crcData = frame.ToArray();
        byte[] crc = Crc16.ComputeChecksum(crcData);
        frame.AddRange(crc);

        byte[] cobsEncoded = CobsEncoding.Encode(frame.ToArray()).ToArray();

        List<byte> cobsChunk = [0x00]; // Start delimiter (no BLE header)
        cobsChunk.AddRange(cobsEncoded);
        cobsChunk.Add(0x00); // End delimiter

        // Then a raw ACK (13 bytes total)
        byte[] rawAckChunk = new byte[]
        {
            0x00, 0x04,                                                        // ACK signature
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,        // 10 padding bytes
            0x02                                                                // Status
        };

        // Act - Send COBS message first, then raw ACK
        _framer.OnChunkReceived(cobsChunk.ToArray());
        _framer.OnChunkReceived(rawAckChunk);

        // Assert - Should receive both messages
        // NOTE: WaitForMessageAsync prioritizes ACKs (checks ACK queue first)
        // This is correct behavior for real-time systems (ACKs confirm commands succeeded)
        R10Message? firstMessage = await _framer.WaitForMessageAsync(TimeSpan.FromMilliseconds(100));
        R10Message? secondMessage = await _framer.WaitForMessageAsync(TimeSpan.FromMilliseconds(100));

        _ = firstMessage.ShouldNotBeNull();
        firstMessage.Type.ShouldBe(R10MessageType.Acknowledgment); // ACK has priority

        _ = secondMessage.ShouldNotBeNull();
        secondMessage.Type.ShouldBe(R10MessageType.Unknown); // COBS message with unknown header
    }

    /// <summary>
    /// Tests Clear() method clears both raw ACK queue and COBS collector queue
    /// </summary>
    [Test]
    public async Task Clear_PendingMessages_AllCleared()
    {
        // Arrange - Queue up a raw ACK (13 bytes total)
        byte[] rawAckChunk = new byte[]
        {
            0x00, 0x04,                                                        // ACK signature
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,        // 10 padding bytes
            0x02                                                                // Status
        };

        _framer.OnChunkReceived(rawAckChunk);

        // Act - Clear before waiting
        _framer.Clear();

        // Assert - Should timeout (message cleared)
        R10Message? message = await _framer.WaitForMessageAsync(TimeSpan.FromMilliseconds(50));
        message.ShouldBeNull();
    }
}
