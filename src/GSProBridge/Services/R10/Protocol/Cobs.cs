namespace GSProBridge.Services.R10.Protocol;

/// <summary>
/// Consistent Overhead Byte Stuffing (COBS) encoding/decoding for R10 command protocol.
/// COBS eliminates zero bytes from data stream to enable zero-byte framing delimiters.
/// </summary>
/// <remarks>
/// Used by R10 DEVICE_INTERFACE_SERVICE to frame command messages with 0x00 header/terminator.
/// Reference: https://en.wikipedia.org/wiki/Consistent_Overhead_Byte_Stuffing
/// </remarks>
public static class Cobs
{
    /// <summary>
    /// Encodes data using COBS algorithm.
    /// </summary>
    /// <param name="input">Input byte sequence to encode</param>
    /// <returns>COBS-encoded byte sequence with no zero bytes</returns>
    /// <remarks>
    /// COBS replaces zero bytes with distance markers indicating next zero position.
    /// Overhead: +1 byte per 254 bytes of input data (worst case: all non-zero).
    /// </remarks>
    public static IEnumerable<byte> Encode(IEnumerable<byte> input)
    {
        var result = new List<byte>();
        int distanceIndex = 0;
        byte distance = 1;

        foreach (byte b in input)
        {
            if (b != 0 && distance < 255)
            {
                result.Add(b);
                distance++;
            }
            else
            {
                result.Insert(distanceIndex, distance);
                distanceIndex = result.Count;
                distance = 1;

                if (b != 0)
                {
                    result.Add(b);
                    distance++;
                }
            }
        }

        if (result.Count != 255 && result.Count > 0)
        {
            result.Insert(distanceIndex, distance);
        }

        return result;
    }

    /// <summary>
    /// Decodes COBS-encoded data back to original byte sequence.
    /// </summary>
    /// <param name="input">COBS-encoded byte sequence</param>
    /// <returns>Decoded original byte sequence with restored zero bytes</returns>
    /// <remarks>
    /// Returns empty sequence if input is malformed (invalid distance markers).
    /// </remarks>
    public static IEnumerable<byte> Decode(IEnumerable<byte> input)
    {
        byte[] inputArray = input.ToArray();
        var result = new List<byte>();
        int distanceIndex = 0;
        byte distance;

        while (distanceIndex < inputArray.Length)
        {
            distance = inputArray[distanceIndex];

            if (inputArray.Length < distanceIndex + distance || distance < 1)
            {
                return new List<byte>();
            }

            if (distance > 1)
            {
                for (byte i = 1; i < distance; i++)
                {
                    result.Add(inputArray[distanceIndex + i]);
                }
            }

            distanceIndex += distance;

            if (distance < 0xFF && distanceIndex < inputArray.Length)
            {
                result.Add(0);
            }
        }

        return result;
    }
}
