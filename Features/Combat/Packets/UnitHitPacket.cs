using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Combat;

/// <summary>Client to host: one of the host's units (its sync id, AreaUnits) took damage from the client's attack, and
/// whether it was left with no health.</summary>
public readonly record struct UnitHitPacket(long UnitId, float Damage, bool Killed) : IPacket
{
    public const byte PacketId = 27;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(UnitId);
        writer.Write(Damage);
        writer.Write(Killed);
    }

    public static UnitHitPacket Read(ref SpanReadWrite reader) => new(
        UnitId: reader.Read<long>(),
        Damage: reader.Read<float>(),
        Killed: reader.ReadBoolean());
}
