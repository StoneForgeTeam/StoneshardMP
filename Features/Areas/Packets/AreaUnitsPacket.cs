using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Areas;

public readonly record struct AreaUnitsPacket(string Place, string Snapshot) : IPacket
{
    public const byte PacketId = 11;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Snapshot);
    }

    public static AreaUnitsPacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Snapshot: reader.ReadString());
}
