using Google.Protobuf;

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
}
