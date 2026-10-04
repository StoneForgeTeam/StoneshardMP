using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>Host to client: one of the host's units (its sync id, AreaUnits) attacks the client's character - resolved in the client's game.</summary>
public readonly record struct EnemyAttackPacket(long UnitId) : IPacket
{
    public const byte PacketId = 28;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(UnitId);

    public static EnemyAttackPacket Read(ref SpanReadWrite reader) => new(UnitId: reader.Read<long>());
}
