using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>Client to host: what the client did put an effect on one of the host's units (its sync id, AreaUnits) in the
/// client's game - the effect's object by name, the duration it was given, and its stage - as a new one
/// (scr_effect_create), or as a refresh (scr_effect_update: its duration set, made if it has fewer than Stage of it). The
/// host puts it on the real one, from the client's stand-in.</summary>
public readonly record struct UnitEffectPacket(long UnitId, string Effect, float Duration, float Stage, bool Refresh) : IPacket
{
    public const byte PacketId = 33;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(UnitId);
        writer.Write(Effect);
        writer.Write(Duration);
        writer.Write(Stage);
        writer.Write(Refresh);
    }

    public static UnitEffectPacket Read(ref SpanReadWrite reader) => new(
        UnitId: reader.Read<long>(),
        Effect: reader.ReadString(),
        Duration: reader.Read<float>(),
        Stage: reader.Read<float>(),
        Refresh: reader.ReadBoolean());
}
