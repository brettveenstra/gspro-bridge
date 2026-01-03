namespace GSProBridge.Bluetooth;

/// <summary>
/// R10 message types based on protocol reverse engineering
/// Protocol uses different headers (88 13, B4 13, B3 13) to distinguish message types
/// </summary>
public enum R10MessageType
{
    /// <summary>
    /// Acknowledgment message (protocol header: 88 13)
    /// Sent by R10 for every request received
    /// Contains counter + empty ack body, no protobuf payload
    /// </summary>
    Acknowledgment,

    /// <summary>
    /// Protobuf response message (protocol header: B4 13)
    /// Contains protobuf payload (WrapperProto)
    /// Examples: SubscribeResponse, shot data notifications
    /// </summary>
    ProtobufResponse,

    /// <summary>
    /// Protobuf request message (protocol header: B3 13)
    /// Commands sent FROM R10 TO PC (rare - unsolicited notifications)
    /// </summary>
    ProtobufRequest,

    /// <summary>
    /// Unknown or malformed message
    /// Unable to determine message type from protocol header
    /// </summary>
    Unknown
}
