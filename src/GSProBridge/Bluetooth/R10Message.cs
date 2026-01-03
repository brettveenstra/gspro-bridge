using System.Collections.Immutable;
using LaunchMonitor.Proto;

namespace GSProBridge.Bluetooth;

/// <summary>
/// Represents a parsed R10 message (ACK, protobuf response, or unknown)
/// </summary>
public record R10Message
{
    /// <summary>
    /// Message type (ACK, protobuf response, etc.)
    /// </summary>
    public required R10MessageType Type
    {
        get;
        init;
    }

    /// <summary>
    /// Message counter from protocol header
    /// Used to correlate requests with responses/ACKs
    /// </summary>
    public ushort Counter
    {
        get;
        init;
    }

    /// <summary>
    /// Parsed protobuf message (for ProtobufResponse/ProtobufRequest types)
    /// Null for ACK messages and unparseable messages
    /// </summary>
    public WrapperProto? Protobuf
    {
        get;
        init;
    }

    /// <summary>
    /// Raw message data (ACK body or protobuf bytes)
    /// Useful for debugging and traffic analysis
    /// </summary>
    public ImmutableArray<byte> RawData
    {
        get;
        init;
    } = [];
}
