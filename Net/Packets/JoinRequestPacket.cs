using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Client -> host: let me into your world, as this player (its name is the key the host keeps its character by).
public readonly record struct JoinRequestPacket(string Name) : IPacket
{
    public const byte PacketId = 14;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Name);

    public static JoinRequestPacket Read(ref SpanReadWrite reader) => new(Name: reader.ReadString());
}
