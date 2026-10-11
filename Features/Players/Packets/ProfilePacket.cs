using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Players;

public readonly record struct ProfilePacket(string Values) : IPacket
{
    public const byte PacketId = 8;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Values);

    public static ProfilePacket Read(ref SpanReadWrite reader) => new(
        Values: reader.ReadString());
}
