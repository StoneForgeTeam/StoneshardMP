using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Host -> everyone: who plays which of the world's player slots (WorldSlots) and who that is (SlotCharacters) - one row
// per player in the session: their session slot, their world slot, its character ("Arna, level 5", or "new character").
// Sent when it changes, and to whoever's come in. The rows are "session|world|character" lines.
public readonly record struct SlotsPacket(string Rows) : IPacket
{
    public const byte PacketId = 39;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Rows);

    public static SlotsPacket Read(ref SpanReadWrite reader) => new(Rows: reader.ReadString());
}
