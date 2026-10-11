using StoneshardMP.Memory;

namespace StoneshardMP.Net.Packets;

/// <summary>A follower to the place's owner: a Deathstinger Jar its player threw let its swarm out here (the game's
/// o_enemy_spawner's cell), for the owner to let it out - the swarm is one of the owner's units.</summary>
public readonly record struct SwarmReleasedPacket(string Place, double X, double Y) : IPacket
{
    public const byte PacketId = 68;

    public byte Id => PacketId;

    public void Write(ref SpanReadWrite writer)
    {
        writer.Write(Place);
        writer.Write(X);
        writer.Write(Y);
    }

    public static SwarmReleasedPacket Read(ref SpanReadWrite reader)
        => new(reader.ReadString(), reader.ReadDouble(), reader.ReadDouble());
}
