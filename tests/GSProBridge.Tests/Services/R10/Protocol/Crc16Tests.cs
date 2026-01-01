using GSProBridge.Services.R10.Protocol;
using Shouldly;

namespace GSProBridge.Tests.Services.R10.Protocol;

/// <summary>
/// Comprehensive tests for CRC-16 checksum computation (polynomial 0xA001).
/// </summary>
[TestFixture]
public class Crc16Tests
{
    [Test]
    public void ComputeChecksum_EmptyInput_ReturnsZeroCrc()
    {
        // Arrange
        byte[] input = Array.Empty<byte>();

        // Act
        byte[] result = Crc16.ComputeChecksum(input);

        // Assert
        result.Length.ShouldBe(2);
        result.ShouldBe(new byte[] { 0x00, 0x00 });
    }

    [Test]
    public void ComputeChecksum_SingleByte_ReturnsValidCrc()
    {
        // Arrange
        byte[] input = new byte[] { 0x42 };

        // Act
        byte[] result = Crc16.ComputeChecksum(input);

        // Assert
        result.Length.ShouldBe(2);
        result.ShouldNotBe(new byte[] { 0x00, 0x00 });
    }

    [Test]
    public void ComputeChecksum_KnownTestVector1_ReturnsExpectedCrc()
    {
        // Arrange - "123456789" ASCII test vector for CRC-16-ANSI
        byte[] input = new byte[] { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 };

        // Act
        byte[] result = Crc16.ComputeChecksum(input);

        // Assert
        result.Length.ShouldBe(2);
        // CRC-16-ANSI of "123456789" = 0xBB3D (little-endian: 0x3D, 0xBB)
        result.ShouldBe(new byte[] { 0x3D, 0xBB });
    }

    [Test]
    public void ComputeChecksum_AllZeros_ReturnsZeroCrc()
    {
        // Arrange
        byte[] input = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        // Act
        byte[] result = Crc16.ComputeChecksum(input);

        // Assert
        result.ShouldBe(new byte[] { 0x00, 0x00 });
    }

    [Test]
    public void ComputeChecksum_AllOnes_ReturnsValidCrc()
    {
        // Arrange
        byte[] input = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

        // Act
        byte[] result = Crc16.ComputeChecksum(input);

        // Assert
        result.Length.ShouldBe(2);
        result.ShouldNotBe(new byte[] { 0x00, 0x00 });
    }

    [Test]
    public void ComputeChecksum_SameInputTwice_ReturnsSameCrc()
    {
        // Arrange
        byte[] input = new byte[] { 0x11, 0x22, 0x33, 0x44 };

        // Act
        byte[] result1 = Crc16.ComputeChecksum(input);
        byte[] result2 = Crc16.ComputeChecksum(input);

        // Assert
        result1.ShouldBe(result2);
    }

    [Test]
    public void ComputeChecksum_DifferentInputs_ReturnsDifferentCrcs()
    {
        // Arrange
        byte[] input1 = new byte[] { 0x11, 0x22, 0x33 };
        byte[] input2 = new byte[] { 0x11, 0x22, 0x34 }; // Last byte different

        // Act
        byte[] result1 = Crc16.ComputeChecksum(input1);
        byte[] result2 = Crc16.ComputeChecksum(input2);

        // Assert
        result1.ShouldNotBe(result2);
    }

    [Test]
    public void ComputeChecksum_OrderMatters_DifferentCrcs()
    {
        // Arrange
        byte[] input1 = new byte[] { 0x11, 0x22 };
        byte[] input2 = new byte[] { 0x22, 0x11 }; // Reversed order

        // Act
        byte[] result1 = Crc16.ComputeChecksum(input1);
        byte[] result2 = Crc16.ComputeChecksum(input2);

        // Assert
        result1.ShouldNotBe(result2);
    }

    [Test]
    public void ComputeChecksum_LargeInput_ReturnsValidCrc()
    {
        // Arrange - 1KB of sequential data
        byte[] input = Enumerable.Range(0, 1024).Select(i =>
        {
            return (byte)(i % 256);
        }).ToArray();

        // Act
        byte[] result = Crc16.ComputeChecksum(input);

        // Assert
        result.Length.ShouldBe(2);
    }

    [Test]
    public void ComputeChecksum_R10MessageFrame_ReturnsValidCrc()
    {
        // Arrange - Simulated R10 message frame (length prefix + "B313" + counter + protobuf)
        byte[] lengthPrefix = BitConverter.GetBytes((ushort)20);
        byte[] b313Magic = new byte[] { 0x42, 0x33, 0x31, 0x33 }; // "B313"
        byte[] counter = BitConverter.GetBytes((ushort)1);
        byte[] padding = new byte[] { 0x00, 0x00 };
        byte[] protobufLength = BitConverter.GetBytes((uint)8);
        byte[] protobufData = new byte[] { 0x0A, 0x05, 0x08, 0x01, 0x10, 0x02, 0x18, 0x03 };

        byte[] fullMessage = lengthPrefix
            .Concat(b313Magic)
            .Concat(counter)
            .Concat(padding)
            .Concat(protobufLength)
            .Concat(protobufLength)
            .Concat(protobufData)
            .ToArray();

        // Act
        byte[] result = Crc16.ComputeChecksum(fullMessage);

        // Assert
        result.Length.ShouldBe(2);
        result.ShouldNotBe(new byte[] { 0x00, 0x00 });
    }

    [Test]
    public void ComputeChecksum_WithAppendedCrc_CanVerifyIntegrity()
    {
        // Arrange
        byte[] data = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        byte[] crc = Crc16.ComputeChecksum(data);
        byte[] dataWithCrc = data.Concat(crc).ToArray();

        // Act - Compute CRC of (data + CRC), should give known constant for valid message
        byte[] verification = Crc16.ComputeChecksum(dataWithCrc);

        // Assert - For correct CRC, computing CRC of (data + CRC) gives 0x0000
        verification.ShouldBe(new byte[] { 0x00, 0x00 });
    }

    [TestCase(new byte[] { 0x00 })]
    [TestCase(new byte[] { 0xFF })]
    [TestCase(new byte[] { 0x11, 0x22 })]
    [TestCase(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 })]
    public void ComputeChecksum_VariousInputs_AlwaysReturnsTwoBytes(byte[] input)
    {
        // Act
        byte[] result = Crc16.ComputeChecksum(input);

        // Assert
        result.Length.ShouldBe(2);
    }

    [Test]
    public void ComputeChecksum_ReturnsLittleEndian_VerifyByteOrder()
    {
        // Arrange - Known test vector "123456789" = CRC 0xBB3D
        byte[] input = new byte[] { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 };

        // Act
        byte[] result = Crc16.ComputeChecksum(input);

        // Assert - Little-endian: LSB first (0x3D), MSB second (0xBB)
        result[0].ShouldBe((byte)0x3D); // Low byte
        result[1].ShouldBe((byte)0xBB); // High byte
    }

    [Test]
    public void ComputeChecksum_BitFlipDetection_DifferentCrcs()
    {
        // Arrange
        byte[] original = new byte[] { 0b10101010 };
        byte[] flipped = new byte[] { 0b10101011 }; // Single bit flipped

        // Act
        byte[] crc1 = Crc16.ComputeChecksum(original);
        byte[] crc2 = Crc16.ComputeChecksum(flipped);

        // Assert - CRC should detect single-bit errors
        crc1.ShouldNotBe(crc2);
    }
}
