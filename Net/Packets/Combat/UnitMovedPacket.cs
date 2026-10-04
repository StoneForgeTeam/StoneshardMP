using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>Client to host: what the client did moved one of the host's units (its sync id, AreaUnits) to a cell - a
/// knockback, a pull - in the client's game; the host moves the real one there.</summary>
public readonly record struct UnitMovedPacket(long UnitId, short CellX, short CellY) : IPacket
{
    public const byte PacketId = 32;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(UnitId);
        writer.Write(CellX);
        writer.Write(CellY);
    }

    public static UnitMovedPacket Read(ref SpanReadWrite reader) => new(
        UnitId: reader.Read<long>(),
        CellX: reader.Read<short>(),
        CellY: reader.Read<short>());
}
