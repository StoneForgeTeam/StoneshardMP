using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Crimes;

/// <summary>Client to host: the client's player assaulted one of the host's NPCs (its sync id, AreaUnits) in the client's
/// game - the game's scr_npc_attack_crime ran on its copy. Warned: past the NPC's warning (its attacks counted up to its
/// tolerance, or "attack" chosen in its warning dialogue), so it's a crime now. The host runs the crime on the real one.</summary>
public readonly record struct AssaultPacket(long UnitId, bool Warned) : IPacket
{
    public const byte PacketId = 47;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(UnitId);
        writer.Write(Warned);
    }

    public static AssaultPacket Read(ref SpanReadWrite reader) => new(
        UnitId: reader.Read<long>(),
        Warned: reader.ReadBoolean());
}
