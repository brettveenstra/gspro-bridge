using Google.Protobuf;
using LaunchMonitor.Proto;

namespace GSProBridge.Bluetooth;

/// <summary>
/// R10 BLE protocol framing, encoding, and chunking
/// </summary>
public static class R10Protocol
{
    private const int MaxChunkSize = 19;
    private static ushort _requestCounter;

    /// <summary>
    /// Frames and chunks a protobuf message for R10 transmission
    /// Protocol: B3 13 [counter:2] 00 00 [length:4] [length:4] [protobuf bytes]
    /// Then: frame with length+CRC, COBS encode, add delimiters, chunk into 19-byte pieces
    /// </summary>
    /// <param name="message">Protobuf message to send</param>
    /// <returns>List of byte chunks ready for BLE transmission</returns>
    public static List<byte[]> FrameAndChunkMessage(IMessage message)
    {
        // 1. Serialize protobuf
        byte[] protoBytes = message.ToByteArray();
        int protoLength = protoBytes.Length;

        // 2. Build protocol message: B3 13 [counter] 00 00 [length] [length] [proto]
        var protocolMessage = new List<byte>
        {
            0xB3,
            0x13
        };
        protocolMessage.AddRange(BitConverter.GetBytes(_requestCounter));
        protocolMessage.Add(0x00);
        protocolMessage.Add(0x00);
        protocolMessage.AddRange(BitConverter.GetBytes(protoLength));
        protocolMessage.AddRange(BitConverter.GetBytes(protoLength));
        protocolMessage.AddRange(protoBytes);

        // 3. Frame: [total_length:2] [protocol_message] [CRC:2]
        ushort frameLength = (ushort)(2 + protocolMessage.Count + 2);
        var framedMessage = new List<byte>();
        framedMessage.AddRange(BitConverter.GetBytes(frameLength));
        framedMessage.AddRange(protocolMessage);
        framedMessage.AddRange(Crc16.ComputeChecksum(framedMessage));

        // 4. COBS encode
        var encoded = new List<byte>
        {
            0x00 // Start delimiter
        };
        encoded.AddRange(CobsEncoding.Encode(framedMessage));
        encoded.Add(0x00); // End delimiter

        // 5. Chunk into 19-byte pieces
        var chunks = new List<byte[]>();
        int offset = 0;
        while (offset < encoded.Count)
        {
            int chunkSize = Math.Min(MaxChunkSize, encoded.Count - offset);
            byte[] chunk = new byte[chunkSize];
            Array.Copy(encoded.ToArray(), offset, chunk, 0, chunkSize);
            chunks.Add(chunk);
            offset += chunkSize;
        }

        _requestCounter++;
        return chunks;
    }

    /// <summary>
    /// Converts bytes to hex string for logging
    /// </summary>
    /// <param name="bytes">Bytes to convert</param>
    /// <returns>Hex string representation</returns>
    public static string ToHexString(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", string.Empty);
    }

    /// <summary>
    /// Reassembles chunks, decodes COBS, and extracts protobuf payload from received message
    /// Reverse of FrameAndChunkMessage
    /// </summary>
    /// <param name="chunks">Received chunks from R10</param>
    /// <returns>Protobuf bytes if successful, null if parsing failed</returns>
    public static byte[]? DecodeReceivedMessage(List<byte[]> chunks)
    {
        try
        {
            // 1. Reassemble chunks into single message
            var reassembled = new List<byte>();
            foreach (byte[] chunk in chunks)
            {
                reassembled.AddRange(chunk);
            }

            // 2. Remove start/end delimiters (0x00)
            if (reassembled.Count < 2 || reassembled[0] != 0x00 || reassembled[^1] != 0x00)
            {
                return null; // Invalid frame delimiters
            }

            byte[] cobsEncoded = reassembled.Skip(1).Take(reassembled.Count - 2).ToArray();

            // 3. COBS decode
            byte[] framedMessage = CobsEncoding.Decode(cobsEncoded).ToArray();

            if (framedMessage.Length < 6) // Minimum: 2 (length) + 2 (B3 13) + 2 (CRC)
            {
                return null; // Message too short
            }

            // 4. Parse frame: [length:2] [protocol_message] [CRC:2]
            ushort frameLength = BitConverter.ToUInt16(framedMessage, 0);

            if (frameLength != framedMessage.Length)
            {
                return null; // Frame length mismatch
            }

            // Extract protocol message (skip length prefix, remove CRC suffix)
            byte[] protocolMessage = framedMessage.Skip(2).Take(framedMessage.Length - 4).ToArray();

            // Verify CRC
            byte[] computedCrc = Crc16.ComputeChecksum(framedMessage.Take(framedMessage.Length - 2).ToArray());
            byte[] receivedCrc = framedMessage.Skip(framedMessage.Length - 2).Take(2).ToArray();

            if (!computedCrc.SequenceEqual(receivedCrc))
            {
                return null; // CRC mismatch
            }

            // 5. Parse protocol message: B3 13 [counter:2] 00 00 [length:4] [length:4] [protobuf]
            if (protocolMessage.Length < 14 || protocolMessage[0] != 0xB3 || protocolMessage[1] != 0x13)
            {
                return null; // Invalid protocol header
            }

            // Extract protobuf payload (skip 14-byte header: B3 13 + counter + 00 00 + 2x length)
            byte[] protobufBytes = protocolMessage.Skip(14).ToArray();

            return protobufBytes;
        }
        catch
        {
            return null; // Parsing failed
        }
    }

    /// <summary>
    /// Attempts to parse received chunks into a WrapperProto message
    /// </summary>
    /// <param name="chunks">Received chunks from R10</param>
    /// <returns>Parsed WrapperProto or null if parsing failed</returns>
    public static WrapperProto? ParseReceivedMessage(List<byte[]> chunks)
    {
        byte[]? protobufBytes = DecodeReceivedMessage(chunks);

        if (protobufBytes == null)
        {
            return null;
        }

        try
        {
            return LaunchMonitor.Proto.WrapperProto.Parser.ParseFrom(protobufBytes);
        }
        catch
        {
            return null; // Protobuf parsing failed
        }
    }
}
