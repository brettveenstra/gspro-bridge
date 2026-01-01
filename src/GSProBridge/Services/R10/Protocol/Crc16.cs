namespace GSProBridge.Services.R10.Protocol;

/// <summary>
/// CRC-16 checksum computation for R10 command protocol message integrity.
/// Uses polynomial 0xA001 (CRC-16-ANSI / CRC-16-IBM reversed).
/// </summary>
/// <remarks>
/// Used by R10 DEVICE_INTERFACE_SERVICE to validate command message integrity.
/// Checksum appended to message before COBS encoding.
/// </remarks>
public static class Crc16
{
    private const ushort Polynomial = 0xA001;
    private static readonly ushort[] _table = new ushort[256];

    /// <summary>
    /// Initializes CRC-16 lookup table for fast computation.
    /// </summary>
    static Crc16()
    {
        ushort value;
        ushort temp;
        for (ushort i = 0; i < _table.Length; ++i)
        {
            value = 0;
            temp = i;
            for (byte j = 0; j < 8; ++j)
            {
                if (((value ^ temp) & 0x0001) != 0)
                {
                    value = (ushort)((value >> 1) ^ Polynomial);
                }
                else
                {
                    value >>= 1;
                }
                temp >>= 1;
            }
            _table[i] = value;
        }
    }

    /// <summary>
    /// Computes CRC-16 checksum for given byte sequence.
    /// </summary>
    /// <param name="bytes">Input byte sequence to checksum</param>
    /// <returns>2-byte CRC-16 checksum (little-endian)</returns>
    /// <remarks>
    /// Returns checksum as byte array for direct appending to message payload.
    /// CRC computed using lookup table for performance.
    /// </remarks>
    public static byte[] ComputeChecksum(IEnumerable<byte> bytes)
    {
        ushort crc = 0;
        foreach (byte b in bytes)
        {
            crc = (ushort)((crc >> 8) ^ _table[(byte)(crc ^ b)]);
        }
        return BitConverter.GetBytes(crc);
    }
}
