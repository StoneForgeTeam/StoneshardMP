using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Host -> everyone: who plays which of the world's player slots (WorldSlots) and who that is (SlotCharacters) - one row
// per player in the session: their session slot, their world slot, and its character - name, level, class and avatar
// (the portrait the save menu shows), or none: a new character (or no save picked yet to say: Known). Sent when it
// changes, and to whoever's come in. The rows are "session|world|known|name|level|class|avatar" lines (name "" for none).
public readonly record struct SlotsPacket(string Rows) : IPacket
{
    public const byte PacketId = 39;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(Rows);

    public static SlotsPacket Read(ref SpanReadWrite reader) => new(Rows: reader.ReadString());
}
