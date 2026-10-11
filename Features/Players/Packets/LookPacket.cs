using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Players;

public readonly record struct LookPacket(string Json) : IPacket
{
    public const byte PacketId = 5;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Json);

    public static LookPacket Read(ref SpanReadWrite reader) => new(
        Json: reader.ReadString());
}
