using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Combat;

/// <summary>A follower to the area's owner: its arrow or bolt stuck in one of the owner's units (its id there) - the
/// ammo's object name and how many - to drop with it when it dies.</summary>
public readonly record struct StuckArrowPacket(long UnitId, string Item, int Count) : IPacket
{
    public const byte PacketId = 74;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(UnitId);
        writer.Write(Item);
        writer.Write(Count);
    }

    public static StuckArrowPacket Read(ref SpanReadWrite reader) => new(
        UnitId: reader.Read<long>(),
        Item: reader.ReadString(),
        Count: reader.Read<int>());
}
