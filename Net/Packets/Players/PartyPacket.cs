using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// What the others' party frames show of us beyond our state (PartyInfo, as text): sent when it changes, and now and
// then anyway.
public readonly record struct PartyPacket(string Values) : IPacket
{
    public const byte PacketId = 26;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Values);

    public static PartyPacket Read(ref SpanReadWrite reader) => new(
        Values: reader.ReadString());
}
