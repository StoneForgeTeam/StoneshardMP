using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

public readonly record struct RejectedPacket(string Reason) : IPacket
{
    public const byte PacketId = 13;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Reason);
    }

    public static RejectedPacket Read(ref SpanReadWrite reader) => new(
        Reason: reader.ReadString());
}
