using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>Host to the client who assaulted one of its NPCs (its sync id, AreaUnits): the NPC warns them - its "stop
/// that" dialogue, shown in the client's game at the end of the turn, as the game shows it to its own player.</summary>
public readonly record struct AssaultWarningPacket(long UnitId) : IPacket
{
    public const byte PacketId = 48;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer) => writer.Write(UnitId);

    public static AssaultWarningPacket Read(ref SpanReadWrite reader) => new(UnitId: reader.Read<long>());
}
