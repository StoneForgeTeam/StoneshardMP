using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Clock;

public readonly record struct WorldTickPacket(
    int Tick,
    byte Source,
    string Clock) : IPacket
{
    public const byte PacketId = 9;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Tick);
        writer.Write(Source);
        writer.Write(Clock);
    }

    public static WorldTickPacket Read(ref SpanReadWrite reader) => new(
        Tick: reader.ReadInt32(),
        Source: reader.ReadByte(),
        Clock: reader.ReadString());
}
