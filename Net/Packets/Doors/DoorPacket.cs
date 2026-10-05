using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>A door in the sender's place (its key: DoorSync.KeyOf) is open, or shut - changed there, or as it is when
/// players come together.</summary>
public readonly record struct DoorPacket(string Place, string Key, bool Open) : IPacket
{
    public const byte PacketId = 34;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Key);
        writer.Write(Open);
    }

    public static DoorPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Key: reader.ReadString(),
        Open: reader.ReadBoolean());
}
