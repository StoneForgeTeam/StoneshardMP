using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>What's been gathered in a place (Place: AreaOwnership's), its things by key (object and position, '\n'
/// between): the items it was built with that are gone from the ground (Items - a herb, a mushroom, a stick picked up)
/// and the bushes and nests already picked (Harvested). Snapshot false: just gathered by the sender - gone everywhere.
/// Snapshot true: the place's owner's as players come together - Items then every one still there (any other is gone),
/// Harvested every one picked.</summary>
public readonly record struct GatherPacket(string Place, bool Snapshot, string Items, string Harvested) : IPacket
{
    public const byte PacketId = 64;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Snapshot);
        writer.Write(Items);
        writer.Write(Harvested);
    }

    public static GatherPacket Read(ref SpanReadWrite reader)
        => new(reader.ReadString(), reader.ReadBoolean(), reader.ReadString(), reader.ReadString());
}
