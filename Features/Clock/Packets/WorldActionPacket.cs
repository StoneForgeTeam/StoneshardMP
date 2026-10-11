using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Clock;

public readonly record struct WorldActionPacket : IPacket
{
    public const byte PacketId = 10;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) { }

    public static WorldActionPacket Read(ref SpanReadWrite reader) => new();
}
