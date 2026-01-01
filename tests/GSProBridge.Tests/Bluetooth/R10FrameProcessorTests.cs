using GSProBridge.Bluetooth;
using LaunchMonitor.Proto;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace GSProBridge.Tests.Bluetooth;

/// <summary>
/// Tests for R10FrameProcessor chunk handling and protobuf message parsing
/// </summary>
[TestFixture]
public class R10FrameProcessorTests
{
    private R10FrameProcessor? _processor;
    private WrapperProto? _receivedMessage;
    private int _messageCount;

    [SetUp]
    public void SetUp()
    {
        _processor = new R10FrameProcessor(NullLogger.Instance);
        _receivedMessage = null;
        _messageCount = 0;
        _processor.MessageReceived += OnMessageReceived;
    }

    [TearDown]
    public void TearDown()
    {
        if (_processor != null)
        {
            _processor.MessageReceived -= OnMessageReceived;
        }
    }

    private void OnMessageReceived(object? sender, WrapperProto wrapper)
    {
        _receivedMessage = wrapper;
        _messageCount++;
    }

    /// <summary>
    /// Tests that short single-chunk messages (13 bytes, no delimiters) are processed as RAW frames.
    /// CRITICAL: R10 sends short responses WITHOUT COBS encoding - they are raw frames.
    /// This is the critical fix for WakeUp/Subscribe ACK messages.
    /// </summary>
    [Test]
    public void ProcessChunk_ShortSingleChunk_WakeUpAck_ProcessesAsRawFrame()
    {
        // Arrange - Real WakeUp ACK from R10 logs (13 bytes, no delimiters, RAW frame)
        // Format: [header:1][payload:12]
        // Payload is RAW frame (NOT COBS-encoded): [0x04][0x00...][status:2]
        byte[] chunk = new byte[]
        {
            0x00, // Header
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 // RAW frame (12 bytes)
        };

        // Act
        _processor!.ProcessChunk(chunk);

        // Assert
        _messageCount.ShouldBe(1, "Short single-chunk RAW frame should be processed immediately without COBS decode");
        _ = _receivedMessage.ShouldNotBeNull();
    }

    /// <summary>
    /// Tests that short single-chunk Subscribe ACK is processed as RAW frame
    /// </summary>
    [Test]
    public void ProcessChunk_ShortSingleChunk_SubscribeAck_ProcessesAsRawFrame()
    {
        // Arrange - Real Subscribe ACK from R10 logs (13 bytes, RAW frame)
        byte[] chunk = new byte[]
        {
            0x00, // Header
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 // RAW frame (12 bytes)
        };

        // Act
        _processor!.ProcessChunk(chunk);

        // Assert
        _messageCount.ShouldBe(1, "Subscribe ACK RAW frame should be processed immediately without COBS decode");
        _ = _receivedMessage.ShouldNotBeNull();
    }

    /// <summary>
    /// Tests that multi-chunk messages with 0x00 delimiters ARE COBS-encoded and processed correctly.
    /// Unlike short single-chunk messages, multi-chunk messages require COBS decode.
    /// </summary>
    [Test]
    public void ProcessChunk_MultiChunkWithDelimiters_ProcessesWithCobsDecode()
    {
        // Arrange - Multi-chunk COBS-encoded message: [0x00][COBS data][0x00]
        // Chunk 1: Header + start delimiter + 17 bytes
        byte[] chunk1 = new byte[19];
        chunk1[0] = 0x00; // Header
        chunk1[1] = 0x00; // Start delimiter
        for (int i = 2; i < 19; i++)
        {
            chunk1[i] = (byte)(i + 0x10); // COBS data
        }

        // Chunk 2: Header + 18 bytes
        byte[] chunk2 = new byte[19];
        chunk2[0] = 0x00; // Header
        for (int i = 1; i < 19; i++)
        {
            chunk2[i] = (byte)(i + 0x20); // More COBS data
        }

        // Chunk 3: Header + final bytes + end delimiter (shorter chunk)
        byte[] chunk3 = new byte[12];
        chunk3[0] = 0x00; // Header
        for (int i = 1; i < 10; i++)
        {
            chunk3[i] = (byte)(i + 0x30); // Final COBS data
        }
        chunk3[11] = 0x00; // End delimiter

        // Act
        _processor!.ProcessChunk(chunk1);
        int countAfterChunk1 = _messageCount;
        countAfterChunk1.ShouldBe(0, "Should not process after chunk 1 (no end delimiter yet)");

        _processor.ProcessChunk(chunk2);
        int countAfterChunk2 = _messageCount;
        countAfterChunk2.ShouldBe(0, "Should not process after chunk 2 (no end delimiter yet)");

        _processor.ProcessChunk(chunk3);

        // Assert
        _messageCount.ShouldBe(1, "Should process after chunk 3 (end delimiter found)");
    }

    /// <summary>
    /// Tests that handshake chunks are skipped
    /// </summary>
    [Test]
    public void ProcessChunk_HandshakeChunk_IsSkipped()
    {
        // Arrange - Handshake chunk from R10 logs (14 bytes)
        byte[] chunk = new byte[]
        {
            0x00, // Header = 0x00 triggers handshake detection
            0x03, 0x17, 0x03, 0xB3, 0x13, 0x01, 0x01, 0x01, 0x02, 0x05, 0x01, 0x01, 0x02 // Payload
        };

        // Act
        _processor!.ProcessChunk(chunk);

        // Assert
        _messageCount.ShouldBe(0, "Handshake chunk should be skipped, not processed");
    }

    /// <summary>
    /// Tests that empty chunks are safely ignored
    /// </summary>
    [Test]
    public void ProcessChunk_EmptyChunk_DoesNotCrash()
    {
        // Arrange
        byte[] chunk = Array.Empty<byte>();

        // Act & Assert
        Should.NotThrow(() =>
        {
            _processor!.ProcessChunk(chunk);
        });
        _messageCount.ShouldBe(0);
    }

    /// <summary>
    /// Tests that multiple short single-chunk messages are processed independently
    /// </summary>
    [Test]
    public void ProcessChunk_MultipleShortMessages_EachProcessedIndependently()
    {
        // Arrange - Two separate WakeUp ACKs
        byte[] chunk1 = new byte[]
        {
            0x00,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02
        };

        byte[] chunk2 = new byte[]
        {
            0x00,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01
        };

        // Act
        _processor!.ProcessChunk(chunk1);
        int countAfterFirst = _messageCount;

        _processor.ProcessChunk(chunk2);
        int countAfterSecond = _messageCount;

        // Assert
        countAfterFirst.ShouldBe(1, "First message should be processed");
        countAfterSecond.ShouldBe(2, "Second message should be processed independently");
    }

    /// <summary>
    /// Tests edge case: short chunk in middle of multi-chunk message should NOT trigger early processing
    /// </summary>
    [Test]
    public void ProcessChunk_ShortChunkWithinMultiChunk_WaitsForEndDelimiter()
    {
        // Arrange - Multi-chunk message where chunk 2 happens to be short
        // Chunk 1: Start with delimiter
        byte[] chunk1 = new byte[19];
        chunk1[0] = 0x00; // Header
        chunk1[1] = 0x00; // Start delimiter
        for (int i = 2; i < 19; i++)
        {
            chunk1[i] = (byte)(i + 0x10);
        }

        // Chunk 2: Short chunk (15 bytes) but NO end delimiter
        byte[] chunk2 = new byte[15];
        chunk2[0] = 0x00; // Header
        for (int i = 1; i < 15; i++)
        {
            chunk2[i] = (byte)(i + 0x20);
        }

        // Chunk 3: Final chunk with end delimiter
        byte[] chunk3 = new byte[10];
        chunk3[0] = 0x00; // Header
        for (int i = 1; i < 9; i++)
        {
            chunk3[i] = (byte)(i + 0x30);
        }
        chunk3[9] = 0x00; // End delimiter

        // Act
        _processor!.ProcessChunk(chunk1);
        _messageCount.ShouldBe(0, "Should not process after chunk 1");

        _processor.ProcessChunk(chunk2);
        // CRITICAL TEST: Short chunk 2 should NOT trigger processing (it has start delimiter from chunk1)
        _messageCount.ShouldBe(0, "Should not process after short chunk 2 (part of multi-chunk message)");

        _processor.ProcessChunk(chunk3);

        // Assert
        _messageCount.ShouldBe(1, "Should only process after final chunk with end delimiter");
    }

    /// <summary>
    /// Tests that frame start delimiter clears accumulated data from previous frame
    /// </summary>
    [Test]
    public void ProcessChunk_NewFrameStart_ProcessesAccumulatedData()
    {
        // Arrange - Chunk 1: short single-chunk message
        byte[] chunk1 = new byte[]
        {
            0x00,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02
        };

        // Chunk 2: Start of new multi-chunk message (has start delimiter)
        byte[] chunk2 = new byte[19];
        chunk2[0] = 0x00; // Header
        chunk2[1] = 0x00; // Start delimiter - should trigger processing of chunk1 first
        for (int i = 2; i < 19; i++)
        {
            chunk2[i] = (byte)(i + 0x10);
        }

        // Act
        _processor!.ProcessChunk(chunk1);
        int countAfterChunk1 = _messageCount;

        _processor.ProcessChunk(chunk2);
        int countAfterChunk2 = _messageCount;

        // Assert
        countAfterChunk1.ShouldBe(1, "First short message should be processed");
        countAfterChunk2.ShouldBe(1, "New frame start should not process incomplete second message yet");
    }
}
