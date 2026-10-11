using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>To the others in the place: a bomb our player threw landed (Bomb: its loot object - o_loot_smokebomb,
/// o_loot_bomb_fire, o_loot_bomb_acid, o_loot_bomb_bee) where it burst, for their games to show and sound it.</summary>
public readonly record struct BombLandedPacket(string Place, string Bomb, double X, double Y) : IPacket
{
    public const byte PacketId = 67;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(Bomb);
        writer.Write(X);
        writer.Write(Y);
    }

    public static BombLandedPacket Read(ref SpanReadWrite reader)
        => new(reader.ReadString(), reader.ReadString(), reader.ReadDouble(), reader.ReadDouble());
}
