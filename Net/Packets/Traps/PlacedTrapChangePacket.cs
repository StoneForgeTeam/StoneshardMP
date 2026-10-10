using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>A follower to the place's owner: its player changed one of the traps players set - set it down, re-armed it
/// or disarmed it (Present: the trap as it now is, "object|x|y|armed|duration"), or picked it up (not Present).</summary>
public readonly record struct PlacedTrapChangePacket(string Place, string Trap, bool Present) : IPacket
{
    public const byte PacketId = 66;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Trap);
        writer.Write(Present);
    }

    public static PlacedTrapChangePacket Read(ref SpanReadWrite reader)
        => new(reader.ReadString(), reader.ReadString(), reader.ReadBoolean());
}
