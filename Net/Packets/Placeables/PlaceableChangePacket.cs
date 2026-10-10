using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

// Follower -> owner: newly placed or removed. Request asks for the full roster. Owner -> the caster: Broken - the owner's
// copy of their spell was broken (an NPC smashed the boulder), so the real one breaks too.
public readonly record struct PlaceableChangePacket(
    string Place, PlaceableChangeKind Kind, PlacedObjectState Object) : IPacket
{
    public const byte PacketId = 44;
    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write((byte)Kind);
        writer.Write(Object.Object ?? "");
        writer.Write(Object.X);
        writer.Write(Object.Y);
        writer.Write(Object.Timestamp);
        writer.Write(Object.Health);
        writer.Write(Object.Duration);
        writer.Write(Object.Caster);
    }

    public static PlaceableChangePacket Read(ref SpanReadWrite reader) => new(
        Place: reader.ReadString(),
        Kind: (PlaceableChangeKind)reader.ReadByte(),
        Object: new PlacedObjectState(
            Object: reader.ReadString(),
            X: reader.ReadDouble(),
            Y: reader.ReadDouble(),
            Timestamp: reader.ReadDouble(),
            Health: reader.ReadDouble(),
            Duration: reader.ReadDouble(),
            Caster: reader.ReadInt32()));
}

public enum PlaceableChangeKind : byte
{
    Added,
    Removed,
    Request,
    Broken,
}
