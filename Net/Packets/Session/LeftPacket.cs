using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

public readonly record struct LeftPacket(byte Slot) : IPacket
{
    public const byte PacketId = 3;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Slot);
    }

    public static LeftPacket Read(ref SpanReadWrite reader) => new(
        Slot: reader.ReadByte());
}
