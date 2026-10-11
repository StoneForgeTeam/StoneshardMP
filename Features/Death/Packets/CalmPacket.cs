using StoneshardMP.Memory;
using StoneshardMP.Net.Packets;

namespace StoneshardMP.Features.Death;

/// <summary>To everyone: a player died in this place, of this faction's settlement ("" for none) - as a reload would in
/// the game, it calms down: whoever runs the place ends its panic (scr_villagePanicOff: its NPCs neutral again), and the
/// host puts the faction's crime record back as its last save has it.</summary>
public readonly record struct CalmPacket(string Place, string Faction) : IPacket
{
    public const byte PacketId = 54;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Faction);
    }

    public static CalmPacket Read(ref SpanReadWrite reader) => new(reader.ReadString(), reader.ReadString());
}
