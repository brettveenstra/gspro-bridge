namespace GSProBridge.Bluetooth;

/// <summary>
/// CRC-16 checksum computation for R10 protocol framing
/// </summary>
/// <remarks>
/// <para>
/// CRC-16 (Modbus) is a standard cyclic redundancy check algorithm published by the Modbus Organization.
/// Uses polynomial 0xA001 (reversed/reflected form of 0x8005), also known as CRC-16-ANSI or CRC-16-IBM.
/// </para>
/// <para>
/// This implementation follows the standard Modbus specification as published in:
/// - Modbus over Serial Line Specification V1.02 (pages 39-43): https://modbus.org/docs/Modbus_over_serial_line_V1_02.pdf
/// - Modbus.org specifications: https://www.modbus.org/specs.php
/// </para>
/// <para>
/// The lookup table approach is the standard optimization technique for CRC computation, providing
/// O(n) performance with minimal overhead. Required by R10 BLE protocol for message integrity validation.
/// </para>
/// </remarks>
public static class Crc16
{
    private const ushort Polynomial = 0xA001;
    private static readonly ushort[] _table = new ushort[256];

    static Crc16()
    {
        for (ushort i = 0; i < _table.Length; ++i)
        {
            ushort value = 0;
            ushort temp = i;
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
    /// Computes CRC-16 checksum for the given bytes
    /// </summary>
    /// <param name="bytes">Bytes to checksum</param>
    /// <returns>2-byte CRC checksum</returns>
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
