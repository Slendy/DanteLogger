using System.Buffers.Binary;

namespace DanteLogger.types;

public class VendorMessage
{
    public ushort MessageClass { get; set; }
    public ushort SequenceNumber { get; set; }
    public byte VersionMajor { get; set; }
    public byte VersionMinor { get; set; }
    public ushort MessageType { get; set; }
    public uint Delay { get; set; }
    public byte[] Body { get; set; } = [];

    public static VendorMessage? Parse(byte[] data)
    {
        var span = data.AsSpan();

        var messageClass = BinaryPrimitives.ReadUInt16BigEndian(data);
        if (messageClass != 0xffff)
        {
            return null;
        }
        var versionMajor = data[0x18];
        var versionMinor = data[0x19];
        var messageType = BinaryPrimitives.ReadUInt16BigEndian(data[0x1A..]);
        var delay = BinaryPrimitives.ReadUInt32BigEndian(data[0x1C..]);
        return new VendorMessage
        {
            MessageClass = messageClass,
            VersionMajor = versionMajor,
            VersionMinor = versionMinor,
            MessageType = messageType,
            Delay = delay,
            Body = data[0x20..],
        };
    }
}