using GSProBridge.Services.R10.Protocol;
using Shouldly;

namespace GSProBridge.Tests.Services.R10.Protocol;

/// <summary>
/// Comprehensive tests for COBS (Consistent Overhead Byte Stuffing) encoding/decoding.
/// </summary>
[TestFixture]
public class CobsTests
{
    [Test]
    public void Encode_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        byte[] input = Array.Empty<byte>();

        // Act
        IEnumerable<byte> result = Cobs.Encode(input);

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void Encode_SingleNonZeroByte_ReturnsFramedByte()
    {
        // Arrange
        byte[] input = new byte[] { 0x42 };

        // Act
        byte[] result = Cobs.Encode(input).ToArray();

        // Assert
        result.ShouldBe(new byte[] { 0x02, 0x42 });
    }

    [Test]
    public void Encode_SingleZeroByte_ReturnsDistanceMarkers()
    {
        // Arrange
        byte[] input = new byte[] { 0x00 };

        // Act
        byte[] result = Cobs.Encode(input).ToArray();

        // Assert
        result.ShouldBe(new byte[] { 0x01, 0x01 });
    }

    [Test]
    public void Encode_NoZeroBytes_AddsDistanceMarkerAtStart()
    {
        // Arrange
        byte[] input = new byte[] { 0x11, 0x22, 0x33 };

        // Act
        byte[] result = Cobs.Encode(input).ToArray();

        // Assert
        result.ShouldBe(new byte[] { 0x04, 0x11, 0x22, 0x33 });
    }

    [Test]
    public void Encode_ZeroByteInMiddle_SplitsWithDistanceMarkers()
    {
        // Arrange
        byte[] input = new byte[] { 0x11, 0x00, 0x22 };

        // Act
        byte[] result = Cobs.Encode(input).ToArray();

        // Assert
        result.ShouldBe(new byte[] { 0x02, 0x11, 0x02, 0x22 });
    }

    [Test]
    public void Encode_MultipleZeroBytes_CreatesMultipleMarkers()
    {
        // Arrange
        byte[] input = new byte[] { 0x00, 0x00, 0x00 };

        // Act
        byte[] result = Cobs.Encode(input).ToArray();

        // Assert
        result.ShouldBe(new byte[] { 0x01, 0x01, 0x01, 0x01 });
    }

    [Test]
    public void Encode_MaxDistanceWithoutZero_UsesMaxMarker()
    {
        // Arrange - 254 non-zero bytes (max distance before marker required)
        byte[] input = Enumerable.Repeat((byte)0xFF, 254).ToArray();

        // Act
        byte[] result = Cobs.Encode(input).ToArray();

        // Assert
        result[0].ShouldBe((byte)0xFF); // Max distance marker
        result.Length.ShouldBe(255); // 1 marker + 254 data bytes
    }

    [Test]
    public void Encode_ResultContainsNoZeroBytes_ValidatesEncoding()
    {
        // Arrange - input with multiple zeros
        byte[] input = new byte[] { 0x11, 0x00, 0x22, 0x00, 0x33 };

        // Act
        byte[] result = Cobs.Encode(input).ToArray();

        // Assert
        result.ShouldNotContain((byte)0x00);
    }

    [Test]
    public void Decode_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        byte[] input = Array.Empty<byte>();

        // Act
        IEnumerable<byte> result = Cobs.Decode(input);

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void Decode_SingleFramedByte_ReturnsOriginalByte()
    {
        // Arrange
        byte[] encoded = new byte[] { 0x02, 0x42 };

        // Act
        byte[] result = Cobs.Decode(encoded).ToArray();

        // Assert
        result.ShouldBe(new byte[] { 0x42 });
    }

    [Test]
    public void Decode_DistanceMarkersForZero_RestoresZeroByte()
    {
        // Arrange
        byte[] encoded = new byte[] { 0x01, 0x01 };

        // Act
        byte[] result = Cobs.Decode(encoded).ToArray();

        // Assert
        result.ShouldBe(new byte[] { 0x00 });
    }

    [Test]
    public void Decode_SplitWithMarkers_RestoresZeroInMiddle()
    {
        // Arrange
        byte[] encoded = new byte[] { 0x02, 0x11, 0x02, 0x22 };

        // Act
        byte[] result = Cobs.Decode(encoded).ToArray();

        // Assert
        result.ShouldBe(new byte[] { 0x11, 0x00, 0x22 });
    }

    [Test]
    public void Decode_InvalidDistance_ReturnsEmpty()
    {
        // Arrange - distance marker exceeds remaining bytes
        byte[] encoded = new byte[] { 0x10, 0x11, 0x22 }; // Says 16 bytes, only 2 available

        // Act
        IEnumerable<byte> result = Cobs.Decode(encoded);

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void Decode_ZeroDistanceMarker_ReturnsEmpty()
    {
        // Arrange - distance marker cannot be zero
        byte[] encoded = new byte[] { 0x00, 0x11 };

        // Act
        IEnumerable<byte> result = Cobs.Decode(encoded);

        // Assert
        result.ShouldBeEmpty();
    }

    [TestCase(new byte[] { })]
    [TestCase(new byte[] { 0x00 })]
    [TestCase(new byte[] { 0x42 })]
    [TestCase(new byte[] { 0x11, 0x22, 0x33 })]
    [TestCase(new byte[] { 0x00, 0x00, 0x00 })]
    [TestCase(new byte[] { 0x11, 0x00, 0x22, 0x00, 0x33 })]
    [TestCase(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC })]
    public void EncodeDecodeRoundTrip_VariousInputs_RestoresOriginalData(byte[] original)
    {
        // Act
        byte[] encoded = Cobs.Encode(original).ToArray();
        byte[] decoded = Cobs.Decode(encoded).ToArray();

        // Assert
        decoded.ShouldBe(original);
    }

    [Test]
    public void EncodeDecodeRoundTrip_LargeDataWithZeros_RestoresOriginal()
    {
        // Arrange - 1KB of data with zeros interspersed
        byte[] original = new byte[1024];
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 256);
        }

        // Act
        byte[] encoded = Cobs.Encode(original).ToArray();
        byte[] decoded = Cobs.Decode(encoded).ToArray();

        // Assert
        decoded.ShouldBe(original);
    }

    [Test]
    public void Encode_RealR10ProtobufExample_ProducesNoZeros()
    {
        // Arrange - simulated R10 protobuf message with zero bytes
        byte[] protobufMessage = new byte[]
        {
            0x0A, 0x05, 0x08, 0x01, 0x10, 0x00, 0x18, 0x02, // Contains 0x00
            0x00, 0x00, 0x00, 0x00                          // Padding zeros
        };

        // Act
        byte[] encoded = Cobs.Encode(protobufMessage).ToArray();

        // Assert
        encoded.ShouldNotContain((byte)0x00);
        encoded.Length.ShouldBeGreaterThan(protobufMessage.Length); // Overhead added
    }
}
