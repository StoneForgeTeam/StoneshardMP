using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

public readonly record struct JoinedPacket(
    byte Slot,
    string Name,
    string Version) : IPacket
{
    public const byte PacketId = 2;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Slot);
        writer.Write(Name);
        writer.Write(Version);
    }

    public static JoinedPacket Read(ref SpanReadWrite reader) => new(
        Slot: reader.ReadByte(),
        Name: reader.ReadString(),
        Version: reader.ReadString());
}
