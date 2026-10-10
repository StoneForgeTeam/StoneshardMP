using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>Owner to a summon's caster: one of the owner's units (its sync id, AreaUnits) attacked the stand-in of the
/// caster's summon (its id in the caster's game) - resolved in the caster's game, its copy of the unit attacking the real
/// summon, as an attack on a player's stand-in is (EnemyAttackPacket).</summary>
public readonly record struct SummonAttackedPacket(long UnitId, long SummonId) : IPacket
{
    public const byte PacketId = 58;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(UnitId);
        writer.Write(SummonId);
    }

    public static SummonAttackedPacket Read(ref SpanReadWrite reader) => new(reader.Read<long>(), reader.Read<long>());
}
