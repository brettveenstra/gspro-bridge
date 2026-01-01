namespace GSProBridge.Bluetooth;

/// <summary>
/// Consistent Overhead Byte Stuffing (COBS) encoding for R10 BLE framing
/// </summary>
/// <remarks>
/// <para>
/// COBS is a published algorithm by Stuart Cheshire and Mary Baker (1997) for encoding data
/// to eliminate zero bytes, enabling zero-byte frame delimiters in serial communication.
/// </para>
/// <para>
/// This implementation follows the standard COBS specification as published in:
/// - SIGCOMM 1997 Conference Paper: http://conferences.sigcomm.org/sigcomm/1997/papers/p062.pdf
/// - Stuart Cheshire's official paper: https://stuartcheshire.org/papers/COBSforToN.pdf
/// - Wikipedia reference: https://en.wikipedia.org/wiki/Consistent_Overhead_Byte_Stuffing
/// </para>
/// <para>
/// The algorithm transforms bytes in range [0,255] to [1,255], using 0x00 as an unambiguous
/// frame delimiter. Required by R10 BLE protocol for message framing.
/// </para>
/// </remarks>
public static class CobsEncoding
{
    /// <summary>
    /// COBS encodes the input bytes (removes 0x00 bytes and adds distance markers)
    /// </summary>
    /// <param name="input">Bytes to encode</param>
    /// <returns>COBS-encoded bytes</returns>
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
            }
        }

        if (result.Count != 255 && result.Count > 0)
        {
            result.Insert(distanceIndex, distance);
        }

        return result;
    }

    /// <summary>
    /// COBS decodes the input bytes (restores 0x00 bytes)
    /// </summary>
    /// <param name="input">COBS-encoded bytes</param>
    /// <returns>Decoded bytes</returns>
    public static IEnumerable<byte> Decode(IEnumerable<byte> input)
    {
        byte[] inputArray = input.ToArray();
        var result = new List<byte>();
        int distanceIndex = 0;

        while (distanceIndex < inputArray.Length)
        {
            byte distance = inputArray[distanceIndex];

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
