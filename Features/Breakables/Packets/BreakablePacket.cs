using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Breakables;

// Something breakable in a place players share (BreakableSync), by its key (BreakableSync.KeyOf), for that place
// (PlayerState.Place): damage it took at the sender (Amount: the HP lost), or it broke there, or - from the place's owner,
// as players come together - what's left of it (Amount: its HP).
public readonly record struct BreakablePacket(string Place, string Key, BreakableKind Kind, double Amount) : IPacket
{
    public const byte PacketId = 40;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Key);
        writer.Write((byte)Kind);
        writer.Write(Amount);
    }

    public static BreakablePacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Key: reader.ReadString(),
        Kind: (BreakableKind)reader.ReadByte(),
        Amount: reader.ReadDouble());
}
