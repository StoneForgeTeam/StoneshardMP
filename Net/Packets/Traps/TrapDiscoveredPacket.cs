using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

public readonly record struct TrapDiscoveredPacket(string Place, string Key) : IPacket
{
    public const byte PacketId = 41;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Key);
    }

    public static TrapDiscoveredPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Key: reader.ReadString());
}
