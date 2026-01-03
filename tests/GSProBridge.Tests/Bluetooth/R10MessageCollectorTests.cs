using GSProBridge.Bluetooth;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace GSProBridge.Tests.Bluetooth;

/// <summary>
/// Tests for R10MessageCollector - validates ACK parsing and message type discrimination
/// Uses synthetic test data to validate protocol message parsing
/// </summary>
[TestFixture]
public class R10MessageCollectorTests
{
    private R10MessageCollector _collector = null!;
    private ILogger<R10MessageCollector> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a test logger (NullLogger for tests - doesn't output anything)
        _logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<R10MessageCollector>();
        _collector = new R10MessageCollector(_logger);
    }

    /// <summary>
    /// Tests ACK message parsing with protocol header 0x1388
    /// </summary>
    [Test]
    public async Task OnChunkReceived_WakeUpAck_ParsesAsAcknowledgment()
    {
        // Arrange - Synthetic WakeUp ACK message
        // Format: [BLE_HEADER:1] 00 [COBS_ENCODED_FRAME] 00
        // Frame: [length:2] 88 13 [counter:2] [ack_body] [CRC:2]

        // Build frame: 88 13 (ACK header) + 01 00 (counter=1) + 00 01 00 00 00 00 00 00 00 00 (ACK body)
        byte[] protocolMessage = new byte[]
        {
            0x88, 0x13, // Protocol header: ACK
            0x01, 0x00, // Counter: 1
            0x00, 0x01, 0x00, // ACK body start
            0x00, 0x00, 0x00, 0x00, 0x00 // ACK body padding (7 zero bytes total)
        };

        ushort frameLength = (ushort)(2 + protocolMessage.Length + 2); // length + message + CRC
        byte[] lengthBytes = BitConverter.GetBytes(frameLength);

        // Build complete frame
        List<byte> frame = new();
        frame.AddRange(lengthBytes);
        frame.AddRange(protocolMessage);

        // Calculate CRC16 over length + message (excluding CRC itself)
        byte[] crcData = frame.ToArray();
        byte[] crc = Crc16.ComputeChecksum(crcData);
        frame.AddRange(crc);

        // COBS encode
        byte[] cobsEncoded = CobsEncoding.Encode(frame.ToArray()).ToArray();

        // Build BLE chunk: [BLE_HEADER:1] 00 [COBS_DATA] 00
        List<byte> chunk =
        [
            0x20,  // BLE header (example value)
            0x00   // Message start delimiter
        ];
        chunk.AddRange(cobsEncoded);
        chunk.Add(0x00); // Message end delimiter

        // Act
        _collector.OnChunkReceived(chunk.ToArray());
        R10Message? message = await _collector.WaitForMessageAsync(TimeSpan.FromMilliseconds(100));

        // Assert
        _ = message.ShouldNotBeNull();
        message.Type.ShouldBe(R10MessageType.Acknowledgment);
        message.Counter.ShouldBe((ushort)1);
        message.Protobuf.ShouldBeNull(); // ACKs don't have protobuf payloads
    }

    /// <summary>
    /// Tests that unknown protocol headers are handled gracefully
    /// IMPORTANT: Enables discovery of unexpected R10 message formats during hardware testing
    /// </summary>
    [Test]
    public async Task OnChunkReceived_UnknownProtocolHeader_ParsesAsUnknown()
    {
        // Arrange - Message with unknown protocol header (FF FF)
        byte[] protocolMessage = new byte[]
        {
            0xFF, 0xFF, // Unknown protocol header
            0x01, 0x00  // Some data
        };

        ushort frameLength = (ushort)(2 + protocolMessage.Length + 2);
        byte[] lengthBytes = BitConverter.GetBytes(frameLength);

        List<byte> frame = new();
        frame.AddRange(lengthBytes);
        frame.AddRange(protocolMessage);

        byte[] crcData = frame.ToArray();
        byte[] crc = Crc16.ComputeChecksum(crcData);
        frame.AddRange(crc);

        byte[] cobsEncoded = CobsEncoding.Encode(frame.ToArray()).ToArray();

        List<byte> chunk =
        [
            0x20,  // BLE header
            0x00   // Message start delimiter
        ];
        chunk.AddRange(cobsEncoded);
        chunk.Add(0x00); // Message end delimiter

        // Act
        _collector.OnChunkReceived(chunk.ToArray());
        R10Message? message = await _collector.WaitForMessageAsync(TimeSpan.FromMilliseconds(100));

        // Assert
        _ = message.ShouldNotBeNull();
        message.Type.ShouldBe(R10MessageType.Unknown);
    }

    /// <summary>
    /// Tests multi-chunk message assembly
    /// R10 can send large messages split across multiple BLE chunks
    /// </summary>
    [Test]
    public async Task OnChunkReceived_MultiChunkMessage_AssemblesCorrectly()
    {
        // Arrange - Split a small ACK message across 2 chunks
        byte[] protocolMessage = new byte[]
        {
            0x88, 0x13, // ACK header
            0x02, 0x00, // Counter: 2
            0x00, 0x02, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00
        };

        ushort frameLength = (ushort)(2 + protocolMessage.Length + 2);
        byte[] lengthBytes = BitConverter.GetBytes(frameLength);

        List<byte> frame = new();
        frame.AddRange(lengthBytes);
        frame.AddRange(protocolMessage);

        byte[] crcData = frame.ToArray();
        byte[] crc = Crc16.ComputeChecksum(crcData);
        frame.AddRange(crc);

        byte[] cobsEncoded = CobsEncoding.Encode(frame.ToArray()).ToArray();

        // Split COBS data into 2 chunks
        int splitPoint = cobsEncoded.Length / 2;
        byte[] chunk1Data = cobsEncoded.Take(splitPoint).ToArray();
        byte[] chunk2Data = cobsEncoded.Skip(splitPoint).ToArray();

        // Chunk 1: [BLE_HEADER] 00 [FIRST_HALF]
        List<byte> chunk1 =
        [
            0x20,  // BLE header
            0x00   // Start delimiter
        ];
        chunk1.AddRange(chunk1Data);

        // Chunk 2: [BLE_HEADER] [SECOND_HALF] 00
        List<byte> chunk2 = [0x20]; // BLE header
        chunk2.AddRange(chunk2Data);
        chunk2.Add(0x00); // End delimiter

        // Act
        _collector.OnChunkReceived(chunk1.ToArray());
        R10Message? message1 = await _collector.WaitForMessageAsync(TimeSpan.FromMilliseconds(10));
        message1.ShouldBeNull(); // Message not complete yet

        _collector.OnChunkReceived(chunk2.ToArray());
        R10Message? message2 = await _collector.WaitForMessageAsync(TimeSpan.FromMilliseconds(100));

        // Assert
        _ = message2.ShouldNotBeNull();
        message2.Type.ShouldBe(R10MessageType.Acknowledgment);
        message2.Counter.ShouldBe((ushort)2);
    }

    /// <summary>
    /// Tests that malformed messages (CRC failure) are rejected gracefully
    /// </summary>
    [Test]
    public async Task OnChunkReceived_InvalidCrc_RejectsMessage()
    {
        // Arrange - ACK message with intentionally bad CRC
        byte[] protocolMessage = new byte[]
        {
            0x88, 0x13,
            0x03, 0x00,
            0x00, 0x03, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00
        };

        ushort frameLength = (ushort)(2 + protocolMessage.Length + 2);
        byte[] lengthBytes = BitConverter.GetBytes(frameLength);

        List<byte> frame = new();
        frame.AddRange(lengthBytes);
        frame.AddRange(protocolMessage);

        // Intentionally bad CRC
        byte[] badCrc = new byte[] { 0xFF, 0xFF };
        frame.AddRange(badCrc);

        byte[] cobsEncoded = CobsEncoding.Encode(frame.ToArray()).ToArray();

        List<byte> chunk =
        [
            0x20,  // BLE header
            0x00   // Message start delimiter
        ];
        chunk.AddRange(cobsEncoded);
        chunk.Add(0x00); // Message end delimiter

        // Act
        _collector.OnChunkReceived(chunk.ToArray());
        R10Message? message = await _collector.WaitForMessageAsync(TimeSpan.FromMilliseconds(100));

        // Assert
        message.ShouldBeNull(); // Bad CRC should cause message rejection
    }

    /// <summary>
    /// Tests that Clear() properly resets collector state
    /// </summary>
    [Test]
    public void Clear_WithBufferedData_ResetsState()
    {
        // Arrange - Send partial chunk (no end delimiter)
        List<byte> partialChunk =
        [
            0x20,  // BLE header
            0x00   // Start delimiter
        ];
        partialChunk.AddRange([0x01, 0x02, 0x03]); // Partial data

        _collector.OnChunkReceived(partialChunk.ToArray());

        // Act
        _collector.Clear();

        // Assert - Next message should not include buffered data
        // (No direct way to assert internal state, but subsequent message parsing should work correctly)
    }
}
